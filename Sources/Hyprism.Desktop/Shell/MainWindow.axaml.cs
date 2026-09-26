// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;
using Hyprism.Desktop.Screens.Instances;
using Hyprism.Desktop.Screens.Settings;

namespace Hyprism.Desktop.Shell;

public sealed partial class MainWindow : Window
{
    private int _startupTransitionVersion;
    private bool _isSectionWarmUpStarted;
    private bool _isStartupLoadingHiding;
    private INotifyPropertyChanged? _observedViewModel;

    private ScaleTransform LauncherShellScale =>
        ((TransformGroup)LauncherShell.RenderTransform!).Children.OfType<ScaleTransform>().Single();
    private TranslateTransform LauncherShellTranslation =>
        ((TransformGroup)LauncherShell.RenderTransform!).Children.OfType<TranslateTransform>().Single();
    private ScaleTransform StartupContentScale =>
        (ScaleTransform)StartupLoadingContent.RenderTransform!;
    private ScaleTransform StartupMarkScale =>
        (ScaleTransform)StartupMark.RenderTransform!;

    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += OnWindowPropertyChanged;
        UpdateWindowStateIcon();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            UpdateWindowStateIcon();
    }

    private void UpdateWindowStateIcon()
    {
        var isRestored = WindowState is WindowState.Maximized or WindowState.FullScreen;
        MaximizeWindowIcon.IsVisible = !isRestored;
        RestoreWindowIcon.IsVisible = isRestored;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;


        if (DataContext is IStartupLoadingState startupViewModel)
        {
            ApplyStartupLoadingState(startupViewModel.IsStartupLoading);

            if (DataContext is MainWindowViewModel { IsStartupLoading: true })
                ScheduleSectionWarmUpAfterRender();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IStartupLoadingState.IsStartupLoading) &&
            DataContext is IStartupLoadingState startupViewModel)
        {
            ApplyStartupLoadingState(startupViewModel.IsStartupLoading);
        }

    }

    private void ApplyStartupLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            // The startup state is replaced once Core has finished preparing
            // the real shell view model. Keep the same visual loading run.
            if (!StartupLoadingScreen.IsVisible || _isStartupLoadingHiding)
                ShowStartupLoading();
            return;
        }

        if (StartupLoadingScreen.IsVisible)
            _ = HideStartupLoadingAsync();
        else
            ShowLauncherImmediately();
    }

    private void ShowStartupLoading()
    {
        _startupTransitionVersion++;
        _isStartupLoadingHiding = false;
        StartupLoadingScreen.IsVisible = true;
        StartupLoadingScreen.IsHitTestVisible = true;
        StartupLoadingScreen.Opacity = 1;
        LauncherShell.IsHitTestVisible = false;
        LauncherShell.Opacity = 0;
        LauncherShellScale.ScaleX = 0.975;
        LauncherShellScale.ScaleY = 0.975;
        LauncherShellTranslation.Y = 12;
        StartupLoadingContent.Opacity = 0;
        StartupBrand.Opacity = 0;
        StartupContentScale.ScaleX = 0.9;
        StartupContentScale.ScaleY = 0.9;
        StartupMarkScale.ScaleX = 1;
        StartupMarkScale.ScaleY = 1;
        StartupAnimation.Start();

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not IStartupLoadingState { IsStartupLoading: true })
                return;

            StartupLoadingContent.Opacity = 1;
            StartupBrand.Opacity = 1;
            StartupContentScale.ScaleX = 1;
            StartupContentScale.ScaleY = 1;
        }, DispatcherPriority.Render);
    }

    private void ScheduleSectionWarmUpAfterRender()
    {
        if (_isSectionWarmUpStarted)
            return;

        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(StartSectionWarmUp, DispatcherPriority.Background),
            DispatcherPriority.Render);
    }

    private async Task HideStartupLoadingAsync()
    {
        var transitionVersion = ++_startupTransitionVersion;
        _isStartupLoadingHiding = true;
        StartupLoadingScreen.IsHitTestVisible = false;
        StartupLoadingContent.Opacity = 0;
        StartupBrand.Opacity = 0;
        StartupContentScale.ScaleX = 0.96;
        StartupContentScale.ScaleY = 0.96;
        StartupMarkScale.ScaleX = 1.08;
        StartupMarkScale.ScaleY = 1.08;
        StartupLoadingScreen.Opacity = 0;
        LauncherShell.Opacity = 1;
        LauncherShellScale.ScaleX = 1;
        LauncherShellScale.ScaleY = 1;
        LauncherShellTranslation.Y = 0;

        await Task.Delay(440);
        if (transitionVersion != _startupTransitionVersion ||
            DataContext is IStartupLoadingState { IsStartupLoading: true })
        {
            return;
        }

        StartupLoadingScreen.IsVisible = false;
        _isStartupLoadingHiding = false;
        LauncherShell.IsHitTestVisible = true;
        StartupAnimation.Stop();
    }

    private void ShowLauncherImmediately()
    {
        _startupTransitionVersion++;
        _isStartupLoadingHiding = false;
        StartupLoadingScreen.IsVisible = false;
        StartupLoadingScreen.IsHitTestVisible = false;
        StartupLoadingScreen.Opacity = 0;
        StartupBrand.Opacity = 0;
        LauncherShell.IsHitTestVisible = true;
        LauncherShell.Opacity = 1;
        LauncherShellScale.ScaleX = 1;
        LauncherShellScale.ScaleY = 1;
        LauncherShellTranslation.Y = 0;
        StartupAnimation.Stop();
    }

    private void StartSectionWarmUp()
    {
        if (_isSectionWarmUpStarted ||
            DataContext is not MainWindowViewModel { IsStartupLoading: true })
            return;

        _isSectionWarmUpStarted = true;
        _ = WarmUpDeferredSectionsAsync();
    }

    private async Task WarmUpDeferredSectionsAsync()
        => await WarmUpDeferredSections(MainSceneSurface);

    internal static async Task WarmUpDeferredSections(Visual root)
    {
        var deferredControls = root.GetVisualDescendants()
            .OfType<DeferredContentControl>()
            .ToArray();
        foreach (var control in deferredControls)
        {
            if (control.IsActive)
                continue;

            control.BeginPreWarm();
            // Yield below the render priority so the layout pass builds the
            // section while the loading animation keeps moving, then hide it
            // again before the next section
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            control.EndPreWarm();
        }
    }


    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel { IsInstances: true } &&
            this.GetVisualDescendants()
                .OfType<InstancesView>()
                .FirstOrDefault()
                ?.TryNavigateBack() == true)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel { IsSettings: true } &&
            this.GetVisualDescendants()
                .OfType<SettingsView>()
                .FirstOrDefault()
                ?.TryCloseCompactContent() == true)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel { IsNews: true, News: { HasSelectedNewsItem: true } } viewModel)
        {
            viewModel.News.CloseNewsArticleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClicked(object? sender, RoutedEventArgs e)
        => ToggleMaximized();

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximized()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

}
