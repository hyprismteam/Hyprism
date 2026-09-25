// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Models;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Hyprism.Desktop.Tests;

/// <summary>
/// Captures launcher screenshots used by the documentation. The test is skipped
/// unless HYPRISM_DOCS_SCREENSHOTS points at an output directory
/// </summary>
public sealed class DocumentationScreenshotTests
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 800;
    private const int CaptureScale = 2;

    [AvaloniaFact]
    public async Task CaptureDocumentationScreenshots()
    {
        var outputDirectory = ResolveOutputDirectory();
        if (outputDirectory is null)
        {
            Assert.Skip("HYPRISM_DOCS_SCREENSHOTS is not set");
            return;
        }

        var mirrorDirectory = Path.Combine(Path.GetTempPath(), $"hyprism-docs-shots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mirrorDirectory);

        try
        {
            await CaptureAsync(
                Path.Combine(outputDirectory!, "en"),
                mirrorDirectory,
                "en-US");
            await CaptureAsync(
                Path.Combine(outputDirectory!, "ru"),
                mirrorDirectory,
                "ru-RU");
        }
        finally
        {
            try
            {
                Directory.Delete(mirrorDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? ResolveOutputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("HYPRISM_DOCS_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return null;
    }

    private static async Task CaptureAsync(
        string outputDirectory,
        string mirrorDirectory,
        string language)
    {
        Directory.CreateDirectory(outputDirectory);
        using var httpClient = new HttpClient();
        var mirrorCatalog = new MirrorCatalog(mirrorDirectory, httpClient);
        mirrorCatalog.Save(new MirrorMeta
        {
            Id = "community-eu",
            Name = "Community EU",
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://eu.example.org/hytale",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "static-list",
                    StaticVersions = [7]
                }
            }
        });
        mirrorCatalog.Save(new MirrorMeta
        {
            Id = "community-us",
            Name = "Community US",
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://us.example.org/hytale",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "static-list",
                    StaticVersions = [7]
                }
            }
        });

        var versions = new Mock<IGameVersionCatalog>();
        versions
            .Setup(service => service.GetVersionListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([72, 71, 70]);
        versions
            .Setup(service => service.ProbeSourceAvailabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorSpeedTestResult
            {
                MirrorId = "probe",
                IsAvailable = true,
                HasVersionsForCurrentPlatform = true,
                PingMs = 34
            });

        using var viewModel = MainWindowViewModelFactory.Create(
            httpClient,
            mirrorCatalog,
            versions.Object,
            language);
        var window = new MainWindow
        {
            Width = WindowWidth,
            Height = WindowHeight,
            DataContext = viewModel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        await WaitFramesAsync(4);

        async Task CapturePageAsync(string route, string fileName, Action? prepare = null)
        {
            prepare?.Invoke();
            viewModel.NavigateCommand.Execute(route);
            await WaitFramesAsync(6);
            Capture(window, Path.Combine(outputDirectory, fileName));
        }

        await CapturePageAsync("instances", "instances.png");
        var instancesView = window.GetVisualDescendants().OfType<InstancesView>().Single();
        viewModel.Instances.OpenInstanceCreatorCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(
            () => !viewModel.Instances.IsInstanceVersionsLoading &&
                  viewModel.Instances.AvailableInstanceVersions.Count > 0,
            "instance creator versions to load");
        await WaitFramesAsync(20);
        Capture(window, Path.Combine(outputDirectory, "instances-creator.png"));
        viewModel.Instances.CloseInstanceCreatorCommand.Execute(null);
        await WaitFramesAsync(16);

        await CapturePageAsync("profiles", "profiles.png");

        await CapturePageAsync("news", "news.png");
        if (viewModel.FeaturedNews is not null)
        {
            await viewModel.FeaturedNews.OpenCommand.ExecuteAsync(null);
            await WaitFramesAsync(10);
            Capture(window, Path.Combine(outputDirectory, "news-article.png"));
            await viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
            await WaitFramesAsync(4);
        }

        await CapturePageAsync("settings", "settings-downloads.png", () =>
            SelectSettingsCategory(viewModel, "downloads"));
        await WaitFramesAsync(8);

        SelectSettingsCategory(viewModel, "general");
        await WaitFramesAsync(4);
        Capture(window, Path.Combine(outputDirectory, "settings-general.png"));

        SelectSettingsCategory(viewModel, "java");
        await WaitFramesAsync(4);
        Capture(window, Path.Combine(outputDirectory, "settings-java.png"));
        var settingsView = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var javaArgumentsModal = settingsView.FindControl<OverlayModal>("JavaArgumentModal");
        Assert.NotNull(javaArgumentsModal);
        viewModel.Settings.ShowAddJavaArgumentCommand.Execute(null);
        viewModel.Settings.NewJavaArgument = "-Dfile.encoding=UTF-8";
        await WaitForModalAsync(javaArgumentsModal!);
        var sheet = javaArgumentsModal!.FindControl<Grid>("OverlayModalSheet");
        Assert.NotNull(sheet);
        using var dialogFrame = RenderAtCaptureScale(sheet!);
        dialogFrame.Save(Path.Combine(outputDirectory, "java-arguments.png"), PngBitmapEncoderOptions.Default);

        window.Close();
        CaptureSelectionControls(outputDirectory, language);
    }

    private static void CaptureSelectionControls(string outputDirectory, string language)
    {
        var isRussian = language.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        var selectionCheck = new CheckBox
        {
            Content = isRussian ? "Показывать экспериментальные моды" : "Show experimental mods",
            IsChecked = true
        };
        selectionCheck.Classes.Add("uiSelectionCheck");
        selectionCheck.Classes.Add("row");

        var disabledCheck = new CheckBox
        {
            Content = isRussian ? "Недоступный параметр" : "Unavailable option",
            IsEnabled = false
        };
        disabledCheck.Classes.Add("uiSelectionCheck");
        disabledCheck.Classes.Add("row");

        var bundledRuntime = CreateSelectionRadio(
            isRussian ? "Встроенная среда Java" : "Bundled Java runtime",
            selected: true);
        var customRuntime = CreateSelectionRadio(
            isRussian ? "Своя среда Java" : "Custom Java runtime",
            selected: false);
        var window = new Window
        {
            Width = 560,
            Height = 380,
            Content = new Border
            {
                Classes = { "card" },
                Padding = new Thickness(24),
                Margin = new Thickness(28),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = isRussian ? "Элементы выбора" : "Selection controls",
                            Classes = { "formLabel" },
                            FontSize = 17
                        },
                        selectionCheck,
                        disabledCheck,
                        bundledRuntime,
                        customRuntime
                    }
                }
            }
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "selection-controls.png"));
        window.Close();
    }

    private static RadioButton CreateSelectionRadio(string label, bool selected)
    {
        var radio = new RadioButton
        {
            GroupName = "JavaRuntime",
            IsChecked = selected,
            Padding = new Thickness(12, 8),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new Border
                    {
                        Classes = { "radioSelectionIndicator" },
                        Child = new Ellipse
                        {
                            Classes = { "radioSelectionDot" },
                            IsVisible = selected
                        }
                    },
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        radio.Classes.Add("uiSelectionRadio");
        return radio;
    }

    private static void SelectSettingsCategory(MainWindowViewModel viewModel, string id)
        => viewModel.Settings.SelectCategoryCommand.Execute(
            viewModel.Settings.Categories.Single(category => category.Id == id));

    private static async Task WaitForModalAsync(OverlayModal modal)
    {
        await AvaloniaTestWait.UntilAsync(
            () => modal.FindControl<Grid>("OverlayModalSheet")?.RenderTransform
                     is Avalonia.Media.TranslateTransform { Y: 0 },
            "Java argument dialog to finish opening");
    }

    private static async Task WaitFramesAsync(int frames)
    {
        for (var index = 0; index < frames; index++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string path)
    {
        using var frame = RenderAtCaptureScale(window);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static RenderTargetBitmap RenderAtCaptureScale(Visual visual)
    {
        // Render the actual views at high density, without enlarging a captured bitmap
        var frame = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(visual.Bounds.Width * CaptureScale),
                (int)Math.Ceiling(visual.Bounds.Height * CaptureScale)),
            new Vector(96 * CaptureScale, 96 * CaptureScale));
        frame.Render(visual);
        return frame;
    }
}
