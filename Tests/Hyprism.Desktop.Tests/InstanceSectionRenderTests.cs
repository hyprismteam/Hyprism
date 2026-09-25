// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Models;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Shell;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class InstanceSectionRenderTests
{
    [AvaloniaFact]
    public async Task ModsBrowseAndLogsSectionsRenderInteractiveRows()
    {
        const string instancePath = "/tmp/hyprism-section-render-test";
        var instance = new InstanceInfo
        {
            Id = "render-instance",
            Name = "Render Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var modManager = new Mock<IModManager>();
        var console = new GameConsoleService();
        var installGate = new TaskCompletionSource<bool>();
        Action<string, string>? installProgress = null;

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Render Player");
        modManager.Setup(service => service.GetModCategoriesAsync()).ReturnsAsync(
        [
            new ModCategory { Id = 0, Name = "All Mods", Slug = "all" },
            new ModCategory { Id = 101, Name = "Blocks", Slug = "blocks" },
            new ModCategory { Id = 102, Name = "Cosmetics/Armor", Slug = "cosmetics-armor" },
            new ModCategory { Id = 103, Name = "Food/Farming", Slug = "food-farming" },
            new ModCategory { Id = 104, Name = "Furniture", Slug = "furniture" },
            new ModCategory { Id = 105, Name = "Gameplay", Slug = "gameplay" },
            new ModCategory { Id = 106, Name = "Library", Slug = "library" },
            new ModCategory { Id = 107, Name = "Miscellaneous", Slug = "miscellaneous" },
            new ModCategory { Id = 108, Name = "Mobs/Characters", Slug = "mobs-characters" },
            new ModCategory { Id = 109, Name = "Prefab", Slug = "prefab" },
            new ModCategory { Id = 110, Name = "Quality of Life", Slug = "quality-of-life" },
            new ModCategory { Id = 111, Name = "Resource Packs", Slug = "resource-packs" },
            new ModCategory { Id = 112, Name = "Utility", Slug = "utility" },
            new ModCategory { Id = 113, Name = "World Gen", Slug = "world-gen" }
        ]);
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns(
        [
            new InstalledMod
            {
                Id = "cf-1",
                CurseForgeId = "1",
                Name = "Rendered Mod",
                Version = "1.0",
                Author = "Author",
                Enabled = true
            }
        ]);
        modManager.Setup(service => service.SearchModsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ModSearchResult
            {
                Mods =
                [
                    new ModInfo
                    {
                        Id = "cf-1",
                        Name = "Rendered Mod",
                        Author = "Author",
                        Summary = "Already installed",
                        LatestFileId = "901"
                    },
                    new ModInfo
                    {
                        Id = "10",
                        Name = "Catalog Mod",
                        Author = "Creator",
                        AuthorAvatarUrl = "https://media.forgecdn.net/avatars/1/2/avatar.png",
                        Summary = "Summary",
                        LatestFileId = "900",
                        LatestFiles =
                        [
                            new ModFileInfo
                            {
                                Id = "900",
                                ModId = "10",
                                DisplayName = "Catalog Mod 1.0",
                                FileName = "catalog-mod.jar"
                            }
                        ]
                    }
                ],
                TotalCount = 2
            });
        modManager.Setup(service => service.GetModFilesAsync("10", 0, 10))
            .ReturnsAsync(new ModFilesResult
            {
                Files =
                [
                    new ModFileInfo
                    {
                        Id = "900",
                        ModId = "10",
                        DisplayName = "Catalog Mod 1.0",
                        FileName = "catalog-mod.jar",
                        ReleaseType = 1,
                        GameVersions = ["release"]
                    },
                    new ModFileInfo
                    {
                        Id = "899",
                        ModId = "10",
                        DisplayName = "Catalog Mod alpha",
                        FileName = "catalog-mod-alpha.jar",
                        ReleaseType = 3,
                        GameVersions = ["release"]
                    },
                    new ModFileInfo
                    {
                        Id = "898",
                        ModId = "10",
                        DisplayName = "Catalog Mod beta",
                        FileName = "catalog-mod-beta.jar",
                        ReleaseType = 2,
                        GameVersions = ["release"]
                    }
                ],
                TotalCount = 3
            });
        modManager.Setup(service => service.GetModDependenciesAsync("10", "900"))
            .ReturnsAsync(
            [
                new ModDependency
                {
                    ModId = "20",
                    Name = "Dependency Mod",
                    Version = "Dependency 1.2",
                    RelationType = CurseForgeDependencyRelationType.RequiredDependency
                }
            ]);
        modManager.Setup(service => service.InstallModFileToInstanceAsync(
                "10", "900", instancePath, It.IsAny<Action<string, string>?>()))
            .Returns((string _, string _, string _, Action<string, string>? progressCallback) =>
            {
                installProgress = progressCallback;
                return installGate.Task;
            });
        console.Append(instance.Id, "ERR", "rendered error line",
            source: "HytaleClient.Application.Program");

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            modManager: modManager.Object,
            gameConsole: console);

        var view = new InstancesView { DataContext = viewModel.Instances };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 1);
        await WaitUntilAsync(() => FindRows(view, "instanceModRow").Any(border => border.IsEffectivelyVisible));

        var modRows = FindRows(view, "instanceModRow").Where(border => border.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(modRows);
        Assert.Contains(modRows[0].GetVisualDescendants(), element => element is CheckBox);
        Assert.Contains(modRows[0].GetVisualDescendants(), element => element is ToggleSwitch);
        Assert.Contains(
            modRows[0].GetVisualDescendants(),
            element => element is Button button && button.Classes.Contains("instanceRowIconButton"));

        viewModel.SelectInstanceSectionCommand.Execute("browse");
        await WaitUntilAsync(() => viewModel.ModCatalogItems.Count == 1);
        Assert.DoesNotContain(viewModel.ModCatalogItems, item => item.Id == "cf-1");
        Assert.Equal(
            ["all", "101", "102", "103", "104", "105", "106", "107", "108", "109", "110", "111", "112", "113"],
            viewModel.ModCatalogCategories.Select(category => category.Value).ToArray());
        Assert.Equal(
            ["All categories", "Blocks", "Cosmetics / Armor", "Food / Farming", "Furniture", "Gameplay", "Library", "Miscellaneous", "Mobs / Characters", "Prefab", "Quality of Life", "Resource Packs", "Utility", "World Gen"],
            viewModel.ModCatalogCategories.Select(category => category.Display).ToArray());
        viewModel.ModCatalogItems.Add(new ModCatalogItemViewModel(
            "11",
            "Incompatible Mod",
            "Other creator",
            "Incompatible summary",
            "901",
            recommendedFileId: "901",
            compatibility: ModCompatibilityStatus.Incompatible,
            compatibilityLabel: "Incompatible"));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() =>
            FindRows(view, "instanceModRow").Count(border =>
                border.IsEffectivelyVisible &&
                border.Classes.Contains("catalog") &&
                border.GetVisualDescendants().OfType<CheckBox>().Any()) == 2);
        var catalogRows = FindRows(view, "instanceModRow")
            .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("catalog"))
            .ToList();
        Assert.Equal(2, catalogRows.Count);
        Assert.All(catalogRows, row => Assert.Equal(80, row.MinHeight));
        Assert.All(catalogRows, row => Assert.Contains(
            row.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("instanceCatalogAuthorBadge")));
        Assert.All(catalogRows, row =>
        {
            var modIcon = Assert.Single(
                row.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("instanceModIcon"));
            Assert.Equal(56, modIcon.Width);
            var authorAvatar = Assert.Single(
                row.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("instanceCatalogAuthorAvatar"));
            Assert.Equal(22, authorAvatar.Width);
            Assert.Contains(authorAvatar.GetVisualDescendants(), descendant => descendant is Image);
        });
        Assert.DoesNotContain(
            catalogRows.SelectMany(row => row.GetVisualDescendants()).OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("instanceCatalogAuthorBrand"));
        Assert.Equal(
            "https://media.forgecdn.net/avatars/1/2/avatar.png",
            viewModel.ModCatalogItems[0].AuthorAvatarUrl);
        Assert.DoesNotContain(
            catalogRows.SelectMany(row => row.GetVisualDescendants()).OfType<Border>(),
            border => border.Classes.Contains("instanceCompatibilityBadge") ||
                      border.Classes.Contains("instanceBadge") &&
                      !border.Classes.Contains("update"));
        var incompatibleRow = Assert.Single(catalogRows, row => row.Classes.Contains("incompatible"));
        Assert.Equal(0.48, incompatibleRow.Opacity);
        var catalogCheck = Assert.Single(
            catalogRows[0].GetVisualDescendants().OfType<CheckBox>());
        Assert.Equal(new Thickness(0), catalogCheck.BorderThickness);
        var checkBackground = Assert.Single(
            catalogCheck.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "SelectionIndicator");
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(checkBackground.Transitions),
            transition => transition is BrushTransition);
        var checkGlyph = Assert.Single(
            catalogCheck.GetVisualDescendants().OfType<PathIcon>(),
            icon => icon.Name == "CheckGlyph");
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(checkGlyph.Transitions),
            transition => transition is DoubleTransition);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(checkGlyph.Transitions),
            transition => transition is TransformOperationsTransition);
        Assert.DoesNotContain(
            FindRows(view, "instanceModRow")
                .Where(border => border.IsEffectivelyVisible)
                .SelectMany(border => border.GetVisualDescendants())
                .OfType<Button>(),
            button => button.Classes.Contains("instanceInstall"));
        var comboCount = view.GetVisualDescendants()
            .OfType<ComboBox>()
            .Count(combo => combo.IsEffectivelyVisible && combo.Classes.Contains("instanceFilterCombo"));
        Assert.Equal(2, comboCount);
        var filterCombos = view.GetVisualDescendants()
            .OfType<ComboBox>()
            .Where(combo => combo.IsEffectivelyVisible && combo.Classes.Contains("instanceFilterCombo"))
            .ToList();
        var searchBox = view.GetVisualDescendants()
            .OfType<TextBox>()
            .Single(textBox => textBox.IsEffectivelyVisible && textBox.Classes.Contains("instanceSearch"));
        Assert.Contains("catalogSearch", searchBox.Classes);
        Assert.Equal(new Thickness(0), searchBox.BorderThickness);
        var searchButton = Assert.IsType<Button>(view.FindControl<Button>("ModCatalogSearchButton"));
        Assert.Contains("hidden", searchButton.Classes);
        Assert.False(searchButton.IsHitTestVisible);
        viewModel.ModCatalogSearchQuery = "abcd";
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("visible", searchButton.Classes);
        Assert.True(searchButton.IsHitTestVisible);
        Assert.Equal(34, searchButton.Width);
        await WaitUntilAsync(() => searchButton.Opacity >= 0.99);
        var searchCallsBeforeButtonClick = modManager.Invocations.Count(invocation =>
            invocation.Method.Name == nameof(IModManager.SearchModsAsync));
        var searchButtonPoint = searchButton.TranslatePoint(
            new Point(searchButton.Bounds.Width / 2, searchButton.Bounds.Height / 2),
            window);
        Assert.NotNull(searchButtonPoint);
        window.MouseMove(searchButtonPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(searchButton.IsPointerOver);
        window.MouseDown(searchButtonPoint!.Value, MouseButton.Left);
        window.MouseUp(searchButtonPoint.Value, MouseButton.Left);
        await WaitUntilAsync(() => modManager.Invocations.Count(invocation =>
            invocation.Method.Name == nameof(IModManager.SearchModsAsync)) > searchCallsBeforeButtonClick);
        viewModel.ModCatalogSearchQuery = string.Empty;
        var filterRow = Assert.IsType<Grid>(searchBox.Parent?.Parent);
        Assert.All(filterCombos, combo => Assert.Same(filterRow, combo.Parent));

        viewModel.ToggleModCatalogSelectionCommand.Execute(viewModel.ModCatalogItems[0]);
        Assert.True(viewModel.HasSelectedCatalogMods);
        var catalogTopInstall = Assert.Single(
            view.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("catalogInstallTopBar"));
        var catalogTopInstallAction = Assert.Single(
            catalogTopInstall.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("catalogInstallTopBarAction"));
        Assert.Contains("visible", catalogTopInstall.Classes);
        Assert.Contains("managerCompactSplitAction", catalogTopInstall.Classes);
        Assert.True(catalogTopInstall.IsHitTestVisible);
        Assert.Contains("managerCompactActionPart", catalogTopInstallAction.Classes);
        Assert.Contains("main", catalogTopInstallAction.Classes);
        Assert.Equal(36, catalogTopInstallAction.Height);
        Assert.Same(viewModel.OpenModCatalogInstallConfirmationCommand, catalogTopInstallAction.Command);
        Assert.Contains(
            catalogTopInstallAction.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == viewModel.InstallLabel);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(catalogTopInstall.Transitions),
            transition => transition is DoubleTransition);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(catalogTopInstall.Transitions),
            transition => transition is TransformOperationsTransition);
        Assert.Contains("hidden", searchButton.Classes);
        Assert.False(searchButton.IsHitTestVisible);
        viewModel.OpenModCatalogInstallConfirmationCommand.Execute(null);
        var installModal = view.FindControl<OverlayModal>("ModCatalogInstallModal");
        Assert.NotNull(installModal);
        Assert.Equal(674, installModal!.ShoulderMaxWidth);
        await WaitUntilAsync(() => viewModel.HasModCatalogInstallConfirmation && installModal!.IsEffectivelyVisible);
        var installTable = view.FindControl<Border>("ModCatalogInstallTable");
        Assert.NotNull(installTable);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => installTable!.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("modCatalogInstallTableRow")));
        Assert.Contains(
            installModal!.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == viewModel.ModCatalogInstallPreviewTitle);
        Assert.Contains(
            installModal.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == viewModel.ModCatalogInstallDependenciesColumn);
        Assert.Equal("Catalog Mod 1.0", viewModel.ModCatalogInstallItems[0].Version);
        await WaitUntilAsync(() => viewModel.ModCatalogInstallItems[0].DependencyCount == 1);
        Assert.Single(
            installTable!.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("modCatalogInstallTableRow"));
        var installRow = Assert.Single(
            installTable.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("modCatalogInstallTableRow"));
        Assert.Contains(
            installRow.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == viewModel.ModCatalogInstallItems[0].Name);
        Assert.Contains(
            installRow.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == viewModel.ModCatalogInstallItems[0].Version);
        Assert.Contains(
            installRow.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Depends on 1 mods");
        var dependencyBadge = Assert.Single(
            installRow.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("modCatalogDependencyBadge"));
        var dependencyPopup = Assert.Single(
            installRow.GetVisualDescendants().OfType<FadingPopup>());
        Assert.Equal("Top", dependencyPopup.Placement.ToString());
        Assert.Equal(-8d, dependencyPopup.VerticalOffset);
        Assert.True(dependencyPopup.IsHoverEnabled);
        Assert.IsType<Border>(dependencyPopup.Child);
        Assert.Equal(
            "(Dependency 1.2)",
            viewModel.ModCatalogInstallItems[0].DependencyItems[0].VersionInParentheses);
        var installConfirmButton = view.FindControl<Button>("ModCatalogInstallConfirmButton");
        Assert.NotNull(installConfirmButton);
        Assert.Same(viewModel.InstallSelectedCatalogModsCommand, installConfirmButton!.Command);
        var installResetButton = view.FindControl<Button>("ModCatalogInstallResetButton");
        Assert.NotNull(installResetButton);
        Assert.Same(viewModel.ClearModCatalogSelectionCommand, installResetButton!.Command);
        Assert.Contains(
            installResetButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("dataActionIcon"));
        viewModel.CloseModCatalogInstallConfirmationCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.HasModCatalogInstallConfirmation);
        viewModel.ToggleModCatalogSelectionCommand.Execute(viewModel.ModCatalogItems[0]);
        Assert.Contains("hidden", catalogTopInstall.Classes);
        Assert.False(catalogTopInstall.IsHitTestVisible);
        viewModel.ToggleModCatalogSelectionCommand.Execute(viewModel.ModCatalogItems[0]);
        Assert.Contains("visible", catalogTopInstall.Classes);

        var installTask = viewModel.InstallSelectedCatalogModsCommand.ExecuteAsync(null);
        var installScreen = view.FindControl<Border>("ModCatalogInstallScreen");
        Assert.NotNull(installScreen);
        await WaitUntilAsync(() => viewModel.IsInstallingSelectedCatalogMods && installScreen!.IsEffectivelyVisible);
        Assert.Equal("0/1", viewModel.ModCatalogInstallProgressText);
        Assert.Single(viewModel.ModCatalogInstallItems);
        installProgress!.Invoke("downloading", "catalog-mod.jar");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(28, viewModel.ModCatalogInstallItems[0].Progress);
        installGate.SetResult(true);
        await installTask;
        await WaitUntilAsync(() => !viewModel.IsInstallingSelectedCatalogMods && !installScreen!.IsVisible);

        var listPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_MOD_CATALOG_LIST_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(listPreviewPath))
        {
            window.CaptureRenderedFrame()!.Save(listPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(listPreviewPath));
        }

        await viewModel.SelectModCatalogPreviewCommand.ExecuteAsync(viewModel.ModCatalogItems[0]);
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewFiles.Count == 3);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Any(items => items.IsEffectivelyVisible && items.Classes.Contains("instancePreviewFiles")));
        var modal = view.FindControl<OverlayModal>("ModCatalogModal");
        var preview = modal?.FindControl<Grid>("OverlayModalSheet");
        Assert.NotNull(preview);
        Assert.True(preview.IsEffectivelyVisible);
        var instancesLayout = view.FindControl<Grid>("InstancesLayout");
        Assert.NotNull(modal);
        Assert.True(modal.IsVisible);
        var blurEffect = Assert.IsType<BlurEffect>(instancesLayout?.Effect);
        Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable<ITransition>>(blurEffect.Transitions));
        Assert.True(view.FindControl<Grid>("ModCatalogSection")?.IsVisible);
        Assert.Contains(
            preview.GetVisualDescendants(),
            element => element is ItemsControl items && items.Classes.Contains("instancePreviewFiles"));
        Assert.Contains(
            preview.GetVisualDescendants(),
            element => element is Border border && border.Classes.Contains("dataTableHeader"));
        Assert.DoesNotContain(
            preview.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("instanceCompatibilitySummary"));
        var modalAuthorAvatar = Assert.Single(
            preview.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("modalAuthorAvatar"));
        Assert.Equal(24, modalAuthorAvatar.Width);
        Assert.Contains(modalAuthorAvatar.GetVisualDescendants(), descendant => descendant is Image);
        var releaseBadges = preview.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("modPreviewReleaseBadge"))
            .ToList();
        var releaseBadge = Assert.Single(releaseBadges, border => border.Classes.Contains("release"));
        var betaBadge = Assert.Single(releaseBadges, border => border.Classes.Contains("beta"));
        var alphaBadge = Assert.Single(releaseBadges, border => border.Classes.Contains("alpha"));
        Assert.Contains("release", releaseBadge.Classes);
        Assert.Equal(new Thickness(0), releaseBadge.BorderThickness);
        Assert.Equal(new Thickness(0), betaBadge.BorderThickness);
        Assert.Equal(new Thickness(0), alphaBadge.BorderThickness);
        Assert.All(
            preview.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("instancePreviewFile")),
            button => Assert.Null(ToolTip.GetTip(button)));
        var curseForgeAction = Assert.Single(
            preview.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("modPreviewCurseForgeAction"));
        Assert.Same(viewModel.OpenCatalogModPageCommand, curseForgeAction.Command);
        Assert.Contains(
            curseForgeAction.GetVisualDescendants(),
            element => element is Avalonia.Controls.Shapes.Path path &&
                path.Classes.Contains("modPreviewCurseForgeIcon"));
        Assert.Equal(HorizontalAlignment.Center, curseForgeAction.HorizontalContentAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, curseForgeAction.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, curseForgeAction.VerticalContentAlignment);
        Assert.NotNull(curseForgeAction.Template);
        var installAction = Assert.Single(
            preview.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("splitMain"));
        Assert.Equal(HorizontalAlignment.Stretch, installAction.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, installAction.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, installAction.VerticalContentAlignment);
        Assert.Equal(150, installAction.MinWidth);
        Assert.Contains(
            installAction.GetVisualDescendants(),
            element => element is Avalonia.Controls.Shapes.Path path &&
                path.Classes.Contains("managerCompactActionIcon"));
        Assert.Contains(
            installAction.GetVisualDescendants(),
            element => element is Grid grid && grid.Classes.Contains("managedActionContent"));
        Assert.NotNull(installAction.Template);
        var imageSwitchButtons = preview.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("instancePreviewImageButton"))
            .ToList();
        Assert.Equal(2, imageSwitchButtons.Count);
        Assert.All(imageSwitchButtons, button =>
        {
            Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
        });
        var imageSwitchSymbols = imageSwitchButtons
            .Select(button => Assert.IsType<Viewbox>(button.Content))
            .ToList();
        Assert.All(imageSwitchSymbols, symbol =>
        {
            Assert.Equal(28, symbol.Width);
            Assert.Equal(HorizontalAlignment.Center, symbol.HorizontalAlignment);
            var canvas = Assert.IsType<Canvas>(symbol.Child);
            Assert.Equal(960, canvas.Width);
            var glyph = Assert.IsType<Avalonia.Controls.Shapes.Path>(canvas.Children.Single());
            Assert.NotNull(glyph.Data);
            Assert.Equal(960, Assert.IsType<TranslateTransform>(glyph.RenderTransform).Y);
        });
        Assert.Equal(180, Assert.IsType<RotateTransform>(imageSwitchSymbols[0].RenderTransform).Angle);
        Assert.Null(imageSwitchSymbols[1].RenderTransform);
        var splitActionGrid = Assert.IsType<Grid>(curseForgeAction.Parent);
        Assert.True(splitActionGrid.ColumnDefinitions[2].Width.Value >
                    splitActionGrid.ColumnDefinitions[0].Width.Value);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(curseForgeAction.Transitions),
            transition => transition is BrushTransition);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(installAction.Transitions),
            transition => transition is BrushTransition);
        var curseForgeIcon = Assert.Single(
            curseForgeAction.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("modPreviewCurseForgeIcon"));
        Assert.Equal(23, curseForgeIcon.Width);
        Assert.Equal(5, Assert.IsType<TranslateTransform>(curseForgeIcon.RenderTransform).Y);
        var shoulderScale = Assert.IsType<ScaleTransform>(
            modal?.FindControl<Grid>("OverlayModalShoulders")?.RenderTransform);
        Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable<ITransition>>(shoulderScale.Transitions));
        var shoulderMask = modal!.FindControl<Border>("OverlayModalShoulderMask");
        Assert.NotNull(shoulderMask);
        Assert.Equal(3, shoulderMask.Height);

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_MOD_CATALOG_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            window.CaptureRenderedFrame()!.Save(previewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(previewPath));
        }

        window.Width = 680;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.Classes.Contains("compact"));
        Assert.Equal(560, modal.SheetMaxWidth);
        Assert.Equal(608, modal.ShoulderMaxWidth);
        Assert.Equal(520, modal.SheetMaxHeight);
        var compactPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_CATALOG_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactPreviewPath))
            window.CaptureRenderedFrame()!.Save(compactPreviewPath, PngBitmapEncoderOptions.Default);
        window.Width = 1180;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.Classes.Contains("wide"));

        Assert.True(view.TryNavigateBack());
        Assert.False(viewModel.HasModCatalogPreview);
        Assert.True(viewModel.IsInstanceBrowseSection);
        await WaitUntilAsync(() => !modal.IsVisible);
        Assert.Equal(0, Assert.IsType<BlurEffect>(instancesLayout?.Effect).Radius);

        Assert.Equal(720, view.FindControl<Grid>("InstalledModsSection")?.MaxWidth);
        Assert.Equal(820, view.FindControl<Grid>("ModCatalogSection")?.MaxWidth);
        Assert.Equal(820, view.FindControl<Grid>("InstanceLogsSection")?.MaxWidth);

        viewModel.SelectInstanceSectionCommand.Execute("logs");
        Assert.Single(viewModel.LogsLines);
        await WaitUntilAsync(() => FindLogLines(view).Any(text => text.IsEffectivelyVisible));
        var logLines = FindLogLines(view).Where(text => text.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(logLines);
        Assert.Contains("rendered error line", logLines[0].Text);
        Assert.Contains("error", logLines[0].Classes);

        await Task.Run(() => console.Append(instance.Id, "INFO", "live rendered line"));
        await WaitUntilAsync(() => FindLogLines(view)
            .Any(text => text.IsEffectivelyVisible && text.Text == "live rendered line"));
        Assert.Equal(2, viewModel.LogsLines.Count);

        var logText = Assert.IsType<TextBox>(
            FindLogLines(view).First(text => text.Text == "live rendered line"));
        logText.SelectAll();
        Assert.Equal("live rendered line", logText.SelectedText);
        var logLevel = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(),
            text => text.Classes.Contains("logLevel") && text.Text == "ERROR");
        Assert.Equal(FontWeight.Bold, logLevel.FontWeight);

        var levelButton = view.FindControl<ToggleButton>("LogsLevelButton");
        var levelPopup = view.FindControl<FadingPopup>("LogsLevelPopup");
        Assert.NotNull(levelButton);
        Assert.NotNull(levelPopup);
        Assert.Equal(40, levelButton!.Height);
        Assert.Equal("Debug", viewModel.Instances.LogsLevelSummary);
        Assert.Equal("+2", viewModel.Instances.LogsAdditionalLevelCountText);
        levelButton.IsChecked = true;
        await WaitUntilAsync(() => levelPopup!.IsOpen);
        var levelChecks = levelPopup!.Child!.GetVisualDescendants()
            .OfType<CheckBox>().Where(check => check.Classes.Contains("logsLevelCheck")).ToList();
        Assert.Equal(4, levelChecks.Count);
        var tracingCheck = levelChecks[3];
        var tracingCenter = tracingCheck.TranslatePoint(
            new Point(tracingCheck.Bounds.Width / 2, tracingCheck.Bounds.Height / 2), window);
        Assert.NotNull(tracingCenter);
        window.MouseDown(tracingCenter!.Value, MouseButton.Left);
        window.MouseUp(tracingCenter.Value, MouseButton.Left);
        await WaitUntilAsync(() => viewModel.Instances.IsLogsTracingEnabled);
        Assert.True(levelPopup.IsRequestedOpen);
        Assert.Equal("+3", viewModel.Instances.LogsAdditionalLevelCountText);
    }

    [AvaloniaFact]
    public async Task ModPreviewModalShowsSkeletonAndPreloadsAllScreenshots()
    {
        const string instancePath = "/tmp/hyprism-preview-skeleton-test";
        var instance = new InstanceInfo
        {
            Id = "preview-instance",
            Name = "Preview Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var modManager = new Mock<IModManager>();
        var console = new GameConsoleService();
        var imageHandler = new CountingImageHandler();

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Preview Player");
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns([]);
        modManager.Setup(service => service.SearchModsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ModSearchResult
            {
                Mods =
                [
                    new ModInfo
                    {
                        Id = "20",
                        Name = "Preview Mod",
                        Author = "Creator",
                        Summary = "Summary",
                        LatestFileId = "910",
                        Screenshots =
                        [
                            new CurseForgeScreenshot { Url = "https://fake.local/a.png" },
                            new CurseForgeScreenshot { Url = "https://fake.local/b.png" }
                        ]
                    }
                ],
                TotalCount = 1
            });
        var filesGate = new TaskCompletionSource();
        modManager.Setup(service => service.GetModFilesAsync("20", 0, 10))
            .Returns(async () =>
            {
                await filesGate.Task;
                return new ModFilesResult
                {
                    Files =
                    [
                        new ModFileInfo
                        {
                            Id = "910",
                            ModId = "20",
                            DisplayName = "Preview Mod 1.0",
                            FileName = "preview-mod.jar",
                            ReleaseType = 1,
                            GameVersions = ["release"]
                        }
                    ],
                    TotalCount = 1
                };
            });

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(imageHandler),
            new StringLocalizer("en-US"),
            modManager: modManager.Object,
            gameConsole: console);

        var view = new InstancesView { DataContext = viewModel.Instances };
        var modal = view.FindControl<OverlayModal>("ModCatalogModal");
        Assert.NotNull(modal);
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectInstanceSectionCommand.Execute("browse");
        await WaitUntilAsync(() => viewModel.ModCatalogItems.Count == 1);
        imageHandler.Requests = 0;

        var openTask = viewModel.SelectModCatalogPreviewCommand.ExecuteAsync(
            viewModel.ModCatalogItems[0]);
        Assert.True(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
        filesGate.SetResult();
        await openTask;

        Assert.True(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
        Assert.False(viewModel.IsModCatalogPreviewFilesContentVisible);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var skeletonPanel = FindPanels(view).Single(panel =>
            panel.Classes.Contains("modPreviewFilesSkeleton"));
        Assert.True(skeletonPanel.IsVisible);
        Assert.True(skeletonPanel.IsEffectivelyVisible);
        var skeletonPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_PREVIEW_SKELETON_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(skeletonPreviewPath))
        {
            window.CaptureRenderedFrame()!.Save(skeletonPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await WaitUntilAsync(() => viewModel.IsModCatalogPreviewFilesContentVisible);
        Assert.False(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var contentPanel = FindPanels(view).Single(panel =>
            panel.Classes.Contains("modPreviewContent"));
        Assert.False(skeletonPanel.IsVisible);
        var items = FindItemsControls(view).Single(items =>
            items.Classes.Contains("instancePreviewFiles"));
        Assert.True(items.IsEffectivelyVisible);
        await WaitUntilAsync(() => contentPanel.Opacity > 0.9);
        var revealedPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_PREVIEW_REVEALED_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(revealedPreviewPath))
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(revealedPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await WaitUntilAsync(() => viewModel.ModCatalogPreviewImage is not null);
        Assert.Equal(2, imageHandler.Requests);
        Assert.False(viewModel.CanShowPreviousModCatalogScreenshot);
        Assert.True(viewModel.CanShowNextModCatalogScreenshot);

        viewModel.ShowNextModCatalogScreenshotCommand.Execute(null);
        Assert.True(viewModel.ShowNextModCatalogScreenshotCommand.CanExecute(null));
        Assert.False(viewModel.CanShowPreviousModCatalogScreenshot);
        Assert.True(viewModel.CanShowNextModCatalogScreenshot);
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewScreenshotIndex == 1);
        await WaitUntilAsync(() => !viewModel.IsModCatalogPreviewImageTransitioning);
        Assert.False(viewModel.IsModCatalogPreviewImageLoading);
        Assert.NotNull(viewModel.ModCatalogPreviewImage);
        Assert.True(viewModel.CanShowPreviousModCatalogScreenshot);
        Assert.False(viewModel.CanShowNextModCatalogScreenshot);
        viewModel.ShowNextModCatalogScreenshotCommand.Execute(null);
        Assert.Equal(1, viewModel.ModCatalogPreviewScreenshotIndex);
        await AvaloniaTestWait.UntilAsync(
            () => imageHandler.Requests == 2,
            "next mod catalog screenshot request to complete");
        Assert.Equal(2, imageHandler.Requests);

        var closingImage = viewModel.ModCatalogPreviewImage;
        viewModel.CloseModCatalogPreviewCommand.Execute(null);
        Assert.False(viewModel.HasModCatalogPreview);
        Assert.True(viewModel.IsModCatalogPreviewMounted);
        Assert.Equal(1d, modal.FindControl<Grid>("OverlayModalSheet")!.Opacity);
        Assert.Same(closingImage, viewModel.ModCatalogPreviewImage);
        var closingPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_PREVIEW_CLOSING_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(closingPreviewPath))
        {
            var closingSheet = Assert.IsType<Grid>(modal.FindControl<Grid>("OverlayModalSheet"));
            var closingTranslation = Assert.IsType<TranslateTransform>(closingSheet.RenderTransform);
            await AvaloniaTestWait.UntilAsync(
                () => closingTranslation.IsAnimating(TranslateTransform.YProperty),
                "mod catalog preview close animation to start");
            window.CaptureRenderedFrame()!.Save(closingPreviewPath, PngBitmapEncoderOptions.Default);
        }
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewImage is null);
        Assert.False(viewModel.IsModCatalogPreviewMounted);
        Assert.False(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
    }

    private static List<ItemsControl> FindItemsControls(InstancesView view)
        => view.GetVisualDescendants().OfType<ItemsControl>().ToList();

    private static List<StackPanel> FindPanels(InstancesView view)
        => view.GetVisualDescendants().OfType<StackPanel>().ToList();

    private static List<Border> FindRows(InstancesView view, string className)
        => view.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains(className))
            .ToList();

    private static List<TextBox> FindLogLines(InstancesView view)
        => view.GetVisualDescendants()
            .OfType<TextBox>()
            .Where(text => text.Classes.Contains("logText"))
            .ToList();

    private static Task WaitUntilAsync(Func<bool> condition)
        => AvaloniaTestWait.UntilAsync(condition, "instance section state to settle");

    private sealed class CountingImageHandler : HttpMessageHandler
    {
        public int Requests;

        private static readonly byte[] SinglePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
            "AAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(SinglePixelPng)
            });
        }
    }
}
