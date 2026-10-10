// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Models;
using Hyprism.Desktop.Screens.Instances;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class ModCatalogPaginationTests
{
    [AvaloniaTheory]
    [InlineData(1180, "curseforge")]
    [InlineData(680, "curseforge")]
    [InlineData(1180, "modifold")]
    [InlineData(680, "modifold")]
    public async Task ScrollingAppendsPagesWithoutMovingExistingRows(int width, string source)
    {
        var manager = new Mock<IModManager>();
        var nextPage = new TaskCompletionSource<ModSearchResult>();
        var lastPage = new TaskCompletionSource<ModSearchResult>();
        var requests = new List<(string Query, int Page)>();
        Task<ModSearchResult> Search(string query, int page)
        {
            requests.Add((query, page));
            return page switch
            {
                0 => Task.FromResult(Page(0)),
                1 => nextPage.Task,
                2 => lastPage.Task,
                _ => throw new InvalidOperationException("Requested a page after the end of the catalog")
            };
        }
        ModSearchResult Page(int page) => new()
        {
            TotalCount = 72,
            HasMore = source == "modifold" ? page < 2 : null,
            Mods = Enumerable.Range(page * 24 + 1, 24).Select(id => new ModInfo
            {
                Id = id.ToString(), Name = $"Catalog mod {id}", Author = "Example author",
                Summary = "A catalog entry with a stable row height.", Source = source
            }).ToList()
        };
        manager.Setup(service => service.GetInstanceInstalledMods(It.IsAny<string>()))
            .Returns([new InstalledMod { Id = "cf-1", CurseForgeId = "1", SourceProjectId = "1", Source = source }]);
        manager.Setup(service => service.GetModCategoriesAsync()).ReturnsAsync([]);
        manager.Setup(service => service.SearchModsAsync(It.IsAny<string>(), It.IsAny<int>(), 24,
                It.IsAny<string[]>(), It.IsAny<int>(), 1))
            .Returns((string query, int page, int _, string[] _, int _, int _) => Search(query, page));
        manager.Setup(service => service.SearchModsAsync("modifold", It.IsAny<string>(), It.IsAny<int>(), 24,
                It.IsAny<string[]>(), It.IsAny<int>(), 1))
            .Returns((string _, string query, int page, int _, string[] _, int _, int _) => Search(query, page));

        using var http = new HttpClient();
        using var shell = MainWindowViewModelFactory.Create(http, instancePath: "/tmp/hyprism-pagination", modManager: manager.Object);
        var model = shell.Instances;
        model.ModCatalogSearchQuery = "initial";
        model.SelectedModCatalogSource = model.ModCatalogSourceOptions.Single(option => option.Value == source);
        var view = new InstancesView { DataContext = model };
        var window = new Window { Width = width, Height = 760, Content = view };
        try
        {
            window.Show();
            await AvaloniaTestWait.UntilAsync(() => view.GetVisualDescendants().OfType<Button>()
                .Any(button => button.Classes.Contains("managerListItem")));
            view.GetVisualDescendants().OfType<Button>().First(button =>
                button.Classes.Contains("managerListItem") && button.DataContext is InstanceItemViewModel { Id: "instance-aurora" })
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            model.SelectInstanceSectionCommand.Execute("browse");
            await AvaloniaTestWait.UntilAsync(() => model.ModCatalogItems.Count == 23 && !model.IsModCatalogLoading);
            await AvaloniaTestWait.UntilAsync(() => view.GetVisualDescendants().OfType<ListBox>()
                .Any(control => control.Name == "ModCatalogList" && control.IsEffectivelyVisible));
            var list = view.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "ModCatalogList");
            await AvaloniaTestWait.UntilAsync(() => list.Bounds.Height > 0 && list.Opacity == 1 &&
                view.FindControl<Grid>("InstanceSectionScreen")!.RenderTransform is TranslateTransform { X: 0 } &&
                view.FindControl<Grid>("InstancesContent")!.RenderTransform is TranslateTransform { X: 0 });
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var originalItems = model.ModCatalogItems.ToArray();
            var viewport = scroll.Viewport;
            model.ModCatalogSearchQuery = "unsubmitted edit";
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height - 180);
            await AvaloniaTestWait.UntilAsync(() => model.IsLoadingMoreModCatalog);
            Assert.Equal(viewport, scroll.Viewport);
            Assert.Single(requests, request => request.Page == 1);

            // Continuing to scroll during the request must keep the latest position.
            scroll.Offset += new Vector(0, 80);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var renderDirectory = Environment.GetEnvironmentVariable("HYPRISM_PAGINATION_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(renderDirectory))
            {
                Directory.CreateDirectory(renderDirectory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(renderDirectory, $"{source}-{width}.png"), PngBitmapEncoderOptions.Default);
            }
            await AvaloniaTestWait.UntilAsync(() => list.GetVisualDescendants().OfType<ListBoxItem>()
                .Any(item => item.TranslatePoint(default, scroll)!.Value.Y >= 0),
                $"rows after scrolling: offset {scroll.Offset}, extent {scroll.Extent}, viewport {scroll.Viewport}, " +
                string.Join("; ", list.GetVisualDescendants().OfType<ListBoxItem>().Select(item =>
                    $"{((ModCatalogItemViewModel?)item.DataContext)?.Id}: {item.TranslatePoint(default, scroll)}")));
            var offset = scroll.Offset;
            var row = list.GetVisualDescendants().OfType<ListBoxItem>()
                .First(item => item.TranslatePoint(default, scroll)!.Value.Y >= 0);
            var rowModel = row.DataContext;
            var rowPosition = row.TranslatePoint(default, scroll)!.Value.Y;
            nextPage.SetResult(Page(1));
            await AvaloniaTestWait.UntilAsync(() => model.ModCatalogItems.Count == 47 && !model.IsLoadingMoreModCatalog);
            Assert.Equal(viewport, scroll.Viewport);
            Assert.Equal(offset.Y, scroll.Offset.Y, precision: 2);
            Assert.Equal(originalItems, model.ModCatalogItems.Take(23));
            var sameRow = list.GetVisualDescendants().OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, rowModel));
            Assert.Equal(rowPosition, sameRow.TranslatePoint(default, scroll)!.Value.Y, precision: 2);
            Assert.Equal(("initial", 1), Assert.Single(requests, request => request.Page == 1));

            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height - 100);
            await AvaloniaTestWait.UntilAsync(() => model.IsLoadingMoreModCatalog);
            Assert.Single(requests, request => request.Page == 2);
            lastPage.SetResult(Page(2));
            await AvaloniaTestWait.UntilAsync(() => model.ModCatalogItems.Count == 71 && !model.IsLoadingMoreModCatalog);
            Assert.False(model.HasMoreModCatalog);
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.DoesNotContain(requests, request => request.Page > 2);
        }
        finally
        {
            nextPage.TrySetResult(Page(1));
            lastPage.TrySetResult(Page(2));
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CatalogContinuesWhenTheFirstPageContainsOnlyInstalledMods()
    {
        var manager = new Mock<IModManager>();
        var pages = new List<int>();
        manager.Setup(service => service.GetInstanceInstalledMods(It.IsAny<string>()))
            .Returns(Enumerable.Range(1, 24).Select(id => new InstalledMod
            {
                Id = $"cf-{id}", CurseForgeId = id.ToString()
            }).ToList());
        manager.Setup(service => service.GetModCategoriesAsync()).ReturnsAsync([]);
        manager.Setup(service => service.SearchModsAsync(It.IsAny<string>(), It.IsAny<int>(), 24,
                It.IsAny<string[]>(), It.IsAny<int>(), 1))
            .Returns((string _, int page, int _, string[] _, int _, int _) =>
            {
                pages.Add(page);
                return Task.FromResult(new ModSearchResult
                {
                    TotalCount = 48,
                    Mods = Enumerable.Range(page * 24 + 1, 24).Select(id => new ModInfo
                    {
                        Id = id.ToString(), Name = $"Catalog mod {id}", Source = "curseforge"
                    }).ToList()
                });
            });
        using var http = new HttpClient();
        using var shell = MainWindowViewModelFactory.Create(http, instancePath: "/tmp/hyprism-pagination", modManager: manager.Object);
        var view = new InstancesView { DataContext = shell.Instances };
        var window = new Window { Width = 1180, Height = 760, Content = view };
        try
        {
            window.Show();
            shell.Instances.SelectInstanceSectionCommand.Execute("browse");
            await AvaloniaTestWait.UntilAsync(() => shell.ModCatalogItems.Count == 24);
            Assert.Equal([0, 1], pages);
            Assert.False(shell.Instances.HasMoreModCatalog);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InstalledModButtonsUseAnimatedBackgrounds()
    {
        using var http = new HttpClient();
        using var shell = MainWindowViewModelFactory.Create(http);
        var model = shell.Instances;
        model.InstalledMods.Add(new InstanceModItemViewModel("mod", "Example mod", "1.0", "Example", true));
        model.InstalledModsSearchQuery = "Example";
        model.InstalledModsSearchQuery = string.Empty;
        var view = new InstancesView { DataContext = model };
        var window = new Window { Width = 1180, Height = 760, Content = view };
        try
        {
            window.Show();
            model.SelectInstanceSectionCommand.Execute("mods");
            await AvaloniaTestWait.UntilAsync(() => view.GetVisualDescendants().OfType<Button>()
                .Any(button => button.Classes.Contains("installedModDeleteAction") && button.IsEffectivelyVisible));
            await AvaloniaTestWait.UntilAsync(() =>
                view.FindControl<Grid>("InstanceSectionScreen")!.RenderTransform is TranslateTransform { X: 0 });
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Classes.Contains("instanceToolbarIconButton") || button.Classes.Contains("installedModDeleteAction")).ToList();
            Assert.Equal(3, buttons.Count);
            foreach (var button in buttons)
            {
                Assert.Contains(button.Transitions!, transition => transition is BrushTransition { Property.Name: "Background" });
                var border = button.GetVisualChildren().OfType<Border>().Single();
                Assert.Equal(button.Background, border.Background);
                var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                var background = button.Background;
                window.MouseMove(center);
                await AvaloniaTestWait.UntilAsync(() => button.IsPointerOver && !Equals(background, button.Background));
                Assert.Equal(button.Background, border.Background);
                window.MouseMove(default);
                await AvaloniaTestWait.UntilAsync(() => !button.IsPointerOver);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
