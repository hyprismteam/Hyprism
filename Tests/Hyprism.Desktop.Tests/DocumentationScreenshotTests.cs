// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Accounts;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Models;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Screens.Profiles;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
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
        versions.Setup(service => service.HasDownloadSources()).Returns(true);
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
        var localizer = new StringLocalizer(language);
        var installedMods = new[]
        {
            new InstanceModItemViewModel("paths", "Better paths", "1.3.0", "Example author", true,
                releaseLabel: localizer["modManager.releaseType.release"])
            {
                IsSelected = true, UpdateVersion = "1.4.0"
            },
            new InstanceModItemViewModel("storage", "Storage tools", "1.0.2", "Example author", false,
                releaseType: 2, releaseLabel: localizer["modManager.releaseType.beta"]),
            new InstanceModItemViewModel("library", "Shared library", "2.1.0", "Example author", true,
                releaseType: 3, releaseLabel: localizer["modManager.releaseType.alpha"])
        };
        foreach (var mod in installedMods)
            viewModel.Instances.InstalledMods.Add(mod);
        viewModel.Instances.InstalledModsSearchQuery = "Example";
        viewModel.Instances.InstalledModsSearchQuery = string.Empty;
        viewModel.Instances.SelectedModCount = 1;
        viewModel.Instances.ModUpdateCount = 1;
        viewModel.Instances.SelectInstanceSectionCommand.Execute("mods");
        await WaitFramesAsync(20);
        Capture(window, Path.Combine(outputDirectory, "installed-mods.png"));
        var selectedInstanceButton = instancesView.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("managerListItem") &&
                button.DataContext is InstanceItemViewModel { Id: "instance-aurora" });
        selectedInstanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Width = 1024;
        window.UpdateLayout();
        await WaitFramesAsync(20);
        instancesView.FindControl<Button>("InstalledModsMoreButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFramesAsync(6);
        Capture(window, Path.Combine(outputDirectory, "installed-mods-compact.png"));
        instancesView.FindControl<FadingPopup>("InstalledModsMenuPopup")!.IsRequestedOpen = false;
        window.Width = WindowWidth;
        window.UpdateLayout();
        viewModel.Instances.RequestModDeletionCommand.Execute(installedMods[0]);
        await AvaloniaTestWait.UntilAsync(
            () => instancesView.FindControl<OverlayModal>("ModDeleteModal")!.IsEffectivelyVisible,
            "mod deletion confirmation to open");
        await WaitFramesAsync(24);
        Capture(window, Path.Combine(outputDirectory, "installed-mod-delete.png"));
        viewModel.Instances.CancelModDeletionCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(
            () => !instancesView.FindControl<OverlayModal>("ModDeleteModal")!.IsEffectivelyVisible,
            "mod deletion confirmation to close");
        viewModel.Instances.ClearInstalledModsSelectionCommand.Execute(null);
        viewModel.Instances.SelectedModCount = 0;
        viewModel.Instances.ModUpdateCount = 0;
        viewModel.Instances.CloseInstanceSectionCommand.Execute(null);
        await WaitFramesAsync(20);
        viewModel.Instances.InstalledMods.Clear();
        viewModel.Instances.InstalledModsSearchQuery = "Example";
        viewModel.Instances.InstalledModsSearchQuery = string.Empty;
        var sampleMods = new[]
        {
            new ModCatalogItemViewModel("paths", "Better paths", "Example author", "", "1.3.0",
                recommendedFileId: "paths-1.3", recommendedVersionLabel: "1.3.0",
                compatibility: ModCompatibilityStatus.Compatible,
                dependencies: [new ModDependency
                {
                    ModId = "library", Name = "Shared library", Version = "2.1.0",
                    RelationType = CurseForgeDependencyRelationType.RequiredDependency
                }]),
            new ModCatalogItemViewModel("storage", "Storage tools", "Example author", "", "1.0.2",
                recommendedFileId: "storage-1.0", recommendedVersionLabel: "1.0.2",
                compatibility: ModCompatibilityStatus.Compatible)
        };
        foreach (var mod in sampleMods)
        {
            viewModel.Instances.ModCatalogItems.Add(mod);
            viewModel.Instances.ToggleModCatalogSelectionCommand.Execute(mod);
        }
        viewModel.Instances.SelectInstanceSectionCommand.Execute("browse");
        await WaitFramesAsync(20);
        await viewModel.Instances.OpenModCatalogInstallConfirmationCommand.ExecuteAsync(null);
        var installPage = instancesView.FindControl<Grid>("InstanceModCatalogInstallPage")!;
        var sectionPages = instancesView.FindControl<Grid>("InstanceSectionPages")!;
        await AvaloniaTestWait.UntilAsync(
            () => installPage.IsEffectivelyVisible && installPage.Opacity >= 0.99 && !sectionPages.IsVisible,
            "mod installation confirmation page to open");
        await WaitFramesAsync(6);
        Capture(window, Path.Combine(outputDirectory, "mod-install-confirmation.png"));
        viewModel.Instances.ClearModCatalogSelectionCommand.Execute(null);
        await AvaloniaTestWait.UntilAsync(() => !installPage.IsVisible,
            "mod installation confirmation page to close");
        viewModel.Instances.CloseInstanceSectionCommand.Execute(null);
        await WaitFramesAsync(20);

        viewModel.Instances.OpenInstanceCreatorCommand.Execute(null);
        await WaitFramesAsync(20);
        var creator = instancesView.FindControl<InstanceCreatorView>("InstanceCreatorContentView")!;
        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.Choice).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.Choice).Opacity >= 0.99,
            "instance creation choice to open");
        creator.FindControl<Button>("InstanceDownloadChoiceButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.Download).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.Download).Opacity >= 0.99,
            "instance download step to open");
        await AvaloniaTestWait.UntilAsync(
            () => !viewModel.Instances.IsInstanceVersionsLoading &&
                  viewModel.Instances.AvailableInstanceVersions.Count > 0,
            "instance creator versions to load");
        await WaitFramesAsync(20);
        Capture(window, Path.Combine(outputDirectory, "instances-creator.png"));
        viewModel.Instances.CloseInstanceCreatorCommand.Execute(null);
        await WaitFramesAsync(16);

        viewModel.Instances.OpenInstanceExportCommand.Execute(null);
        await WaitFramesAsync(20);
        Capture(window, Path.Combine(outputDirectory, "instances-export.png"));
        creator.FindControl<Button>("InstanceExportBuildButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.ExportFormat).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.ExportFormat).Opacity >= 0.99,
            "instance export format step to open");
        Capture(window, Path.Combine(outputDirectory, "instances-export-format.png"));
        viewModel.Instances.CloseInstanceCreatorCommand.Execute(null);
        await WaitFramesAsync(16);

        viewModel.Instances.OpenInstanceCreatorCommand.Execute(null);
        await WaitFramesAsync(20);
        creator.FindControl<Button>("InstanceImportChoiceButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AvaloniaTestWait.UntilAsync(
            () => creator.StepControl(InstanceWizardStage.Import).IsEffectivelyVisible &&
                  creator.StepControl(InstanceWizardStage.Import).Opacity >= 0.99,
            "instance import step to open");
        Capture(window, Path.Combine(outputDirectory, "instances-import.png"));
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

        var settingsView = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var gpuPicker = settingsView.GetVisualDescendants()
            .OfType<FadingComboBox>()
            .Single(comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.Settings.GpuPreferences));
        gpuPicker.IsDropDownOpen = true;
        await WaitFramesAsync(8);
        Capture(window, Path.Combine(outputDirectory, "settings-gpu-picker.png"));
        gpuPicker.IsDropDownOpen = false;
        await WaitFramesAsync(4);

        SelectSettingsCategory(viewModel, "java");
        await WaitFramesAsync(4);
        Capture(window, Path.Combine(outputDirectory, "settings-java.png"));
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
        var instancePath = Path.Combine(mirrorDirectory, "instance-" + language);
        Directory.CreateDirectory(instancePath);
        using (var worldsViewModel = MainWindowViewModelFactory.Create(
                   httpClient, mirrorCatalog, versions.Object, language, instancePath: instancePath))
        {
            var worldsWindow = new MainWindow
            {
                Width = WindowWidth,
                Height = WindowHeight,
                DataContext = worldsViewModel
            };
            worldsWindow.Show();
            worldsViewModel.NavigateCommand.Execute("instances");
            worldsViewModel.Instances.SelectInstanceSectionCommand.Execute("worlds");
            await AvaloniaTestWait.UntilAsync(() => !worldsViewModel.Instances.IsInstanceWorldsLoading,
                "initial worlds scan to finish");
            await WaitFramesAsync(20);
            Capture(worldsWindow, Path.Combine(outputDirectory, "worlds-empty.png"));
            var savesPath = Path.Combine(instancePath, "UserData", "Saves");
            var worldNames = new[] { "Aurora valley", "Creative islands", "Expedition" };
            for (var index = 0; index < worldNames.Length; index++)
            {
                var worldPath = Path.Combine(savesPath, worldNames[index]);
                var chunksPath = Path.Combine(worldPath, "universe", "worlds", "default", "chunks");
                Directory.CreateDirectory(chunksPath);
                var chunkPath = Path.Combine(chunksPath, "sample.region");
                using (var chunk = File.Create(chunkPath))
                    chunk.SetLength((index + 1) * 1024 * 1024);
                var modified = new DateTime(2026, 1, 20, 12 + index, 30, 0, DateTimeKind.Local);
                if (index < 2)
                {
                    var previewPath = Path.Combine(worldPath, "preview.png");
                    CreateWorldPreview(previewPath, index);
                    File.SetLastWriteTime(previewPath, modified);
                }
                File.SetLastWriteTime(chunkPath, modified);
                foreach (var directory in Directory.EnumerateDirectories(worldPath, "*", SearchOption.AllDirectories))
                    Directory.SetLastWriteTime(directory, modified);
                Directory.SetLastWriteTime(worldPath, modified);
            }
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Count == 3 &&
                !worldsViewModel.Instances.IsInstanceWorldsLoading, "created saves to appear from filesystem events");
            var firstPreview = worldsViewModel.Instances.InstanceWorlds.Single(world => world.Name == worldNames[0]).Preview;
            Assert.NotNull(firstPreview);
            Assert.False(worldsViewModel.Instances.InstanceWorlds.Single(world => world.Name == worldNames[2]).HasPreview);
            var updatedPreviewPath = Path.Combine(savesPath, worldNames[0], "preview.png");
            CreateWorldPreview(updatedPreviewPath, 2);
            File.SetLastWriteTime(updatedPreviewPath, new DateTime(2026, 1, 20, 12, 45, 0, DateTimeKind.Local));
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Any(world =>
                world.Name == worldNames[0] && world.Preview is not null && !ReferenceEquals(world.Preview, firstPreview)),
                "overwritten world preview to update from filesystem events");
            var modifiedChunkPath = Path.Combine(savesPath, worldNames[0], "universe", "worlds", "default", "chunks", "sample.region");
            using (var chunk = File.OpenWrite(modifiedChunkPath))
                chunk.SetLength(4 * 1024 * 1024);
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Any(world =>
                world.Name == worldNames[0] && world.Size == "4 MB"), "changed save size to update from filesystem events");
            using (var chunk = File.OpenWrite(modifiedChunkPath))
                chunk.SetLength(1024 * 1024);
            File.SetLastWriteTime(modifiedChunkPath, new DateTime(2026, 1, 20, 12, 30, 0, DateTimeKind.Local));
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Any(world =>
                world.Name == worldNames[0] && world.Size == "1 MB"), "restored save size to update");
            var originalPath = Path.Combine(savesPath, "Expedition");
            var renamedPath = Path.Combine(savesPath, "Expedition renamed");
            Directory.Move(originalPath, renamedPath);
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Any(world =>
                world.Name == "Expedition renamed"), "renamed save to update from filesystem events");
            Directory.Move(renamedPath, originalPath);
            var temporaryPath = Path.Combine(savesPath, "Temporary world");
            Directory.CreateDirectory(temporaryPath);
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Count == 4,
                "temporary save to appear");
            Directory.Delete(temporaryPath);
            await AvaloniaTestWait.UntilAsync(() => worldsViewModel.Instances.InstanceWorlds.Count == 3 &&
                worldsViewModel.Instances.InstanceWorlds.All(world => world.Name != "Expedition renamed") &&
                !worldsViewModel.Instances.IsInstanceWorldsLoading, "removed save to disappear from filesystem events");
            await WaitFramesAsync(20);
            Capture(worldsWindow, Path.Combine(outputDirectory, "worlds.png"));
            var worldInstanceButton = worldsWindow.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("managerListItem") &&
                    button.DataContext is InstanceItemViewModel { Id: "instance-aurora" });
            worldInstanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            worldsWindow.Width = 1024;
            worldsWindow.UpdateLayout();
            await WaitFramesAsync(20);
            Capture(worldsWindow, Path.Combine(outputDirectory, "worlds-compact.png"));
            worldsWindow.Width = WindowWidth;
            worldsWindow.UpdateLayout();
            worldsViewModel.Instances.CloseInstanceSectionCommand.Execute(null);
            await WaitFramesAsync(20);
            worldsWindow.Close();
        }
        using (var emptyViewModel = MainWindowViewModelFactory.Create(
                   httpClient, mirrorCatalog, versions.Object, language, emptyInstances: true))
        {
            var emptyWindow = new MainWindow
            {
                Width = WindowWidth,
                Height = WindowHeight,
                DataContext = emptyViewModel
            };
            emptyWindow.Show();
            emptyViewModel.NavigateCommand.Execute("instances");
            await WaitFramesAsync(8);
            Capture(emptyWindow, Path.Combine(outputDirectory, "instances-empty.png"));
            emptyWindow.Close();
        }

        versions.Setup(service => service.HasDownloadSources()).Returns(false);
        using (var emptyViewModel = MainWindowViewModelFactory.Create(
                   httpClient, mirrorCatalog, versions.Object, language, emptyInstances: true))
        {
            var emptyWindow = new MainWindow
            {
                Width = WindowWidth,
                Height = WindowHeight,
                DataContext = emptyViewModel
            };
            emptyWindow.Show();
            emptyViewModel.NavigateCommand.Execute("instances");
            await WaitFramesAsync(8);
            Capture(emptyWindow, Path.Combine(outputDirectory, "instances-no-sources.png"));
            emptyWindow.Close();
        }

        CaptureSelectionControls(outputDirectory, language);
        await CaptureOnboardingAsync(outputDirectory, language);
    }

    private static async Task CaptureOnboardingAsync(string outputDirectory, string language)
    {
        var localizer = new StringLocalizer(language);
        var settingsStore = new Mock<IDesktopSettingsStore>();
        settingsStore.SetupProperty(store => store.Language, language);
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var profileRepository = new Mock<IProfileRepository>();
        profileRepository.Setup(repository => repository.GetProfiles()).Returns([]);
        using var settings = new SettingsViewModel(
            settingsStore.Object, uriLauncher.Object, localizer);
        using var profiles = new ProfilesViewModel(
            new Mock<IProfileManager>().Object,
            profileRepository.Object,
            uriLauncher.Object,
            localizer);
        using var onboarding = new OnboardingViewModel(
            settingsStore.Object,
            settings,
            profiles,
            new Mock<IGameVersionCatalog>().Object,
            localizer,
            () => { });
        var view = new OnboardingView { DataContext = onboarding };
        var window = new Window
        {
            Width = WindowWidth,
            Height = WindowHeight,
            Content = view
        };
        window.Show();

        async Task ClickAndWaitAsync(string buttonName)
        {
            view.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFramesAsync(28);
        }

        await ClickAndWaitAsync("WelcomeNextButton");
        Capture(window, Path.Combine(outputDirectory, "onboarding-language.png"));
        await ClickAndWaitAsync("LanguageNextButton");
        Capture(window, Path.Combine(outputDirectory, "onboarding-appearance.png"));
        await ClickAndWaitAsync("AppearanceNextButton");
        Capture(window, Path.Combine(outputDirectory, "onboarding-profile-choice.png"));
        await ClickAndWaitAsync("ChooseOfflineButton");
        Capture(window, Path.Combine(outputDirectory, "onboarding-download-warning.png"));
        await ClickAndWaitAsync("WarningContinueButton");
        Assert.True(view.FindControl<StackPanel>("OfflineNameContent")!.IsEffectivelyVisible);
        profiles.OfflineProfileName = "ExamplePlayer";
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDirectory, "onboarding-offline-name.png"));
        await ClickAndWaitAsync("OfflineNameBackButton");
        await ClickAndWaitAsync("WarningBackButton");
        await ClickAndWaitAsync("ChooseOfficialButton");
        Assert.True(view.FindControl<StackPanel>("OfficialContent")!.IsEffectivelyVisible);
        Capture(window, Path.Combine(outputDirectory, "onboarding-official-sign-in.png"));
        window.Close();
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

    private static void CreateWorldPreview(string path, int variant)
    {
        var preview = new Grid
        {
            Width = 320, Height = 180,
            Background = Avalonia.Media.Brush.Parse(variant == 1 ? "#758CAA" : "#9BC4C7")
        };
        preview.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M0,160 L70,70 L140,135 L210,50 L320,140 L320,180 L0,180 Z"),
            Fill = Avalonia.Media.Brush.Parse(variant == 2 ? "#487566" : "#60736A")
        });
        preview.Children.Add(new Border
        {
            Height = 36, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Avalonia.Media.Brush.Parse(variant == 1 ? "#B59D71" : "#73936B")
        });
        preview.Measure(new Size(320, 180));
        preview.Arrange(new Rect(0, 0, 320, 180));
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 180));
        bitmap.Render(preview);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

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
