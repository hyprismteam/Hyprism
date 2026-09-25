// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Hyprism.Desktop.Integrations.GitHub;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Shell;
using Hyprism.Core;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Mods;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Accounts;
using Hyprism.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Hyprism.Desktop;

public sealed partial class App : Application
{
    private MainWindowViewModel? _mainWindowViewModel;
    private readonly CancellationTokenSource _bootstrapCancellation = new();
    private Task? _bootstrapTask;

    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = DesktopRuntime.Services;
            var settings = services.GetRequiredService<IDesktopSettingsStore>();

            var localizer = new StringLocalizer(settings.Language);
            if (!string.Equals(settings.Language, localizer.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                settings.Language = localizer.CurrentLanguage;

            var mainWindow = new MainWindow();
            var uriLauncher = new ExternalUriLauncher(() => mainWindow);
            var filePicker = new FilePicker(() => mainWindow);
            mainWindow.DataContext = new StartupLoadingViewModel(localizer);
            desktop.MainWindow = mainWindow;

            desktop.Exit += OnDesktopExit;
            // Let Avalonia show the lightweight startup view before bootstrap
            // begins. InitializeAsync starts its blocking work on a worker.
            base.OnFrameworkInitializationCompleted();

            _bootstrapTask = InitializeAsync(
                services,
                mainWindow,
                settings,
                uriLauncher,
                filePicker,
                localizer,
                _bootstrapCancellation.Token);
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _bootstrapCancellation.Cancel();
        _mainWindowViewModel?.Dispose();
        (DesktopRuntime.Services as IDisposable)?.Dispose();
        Logger.Shutdown();
    }

    private async Task InitializeAsync(
        IServiceProvider services,
        MainWindow mainWindow,
        IDesktopSettingsStore settings,
        IExternalUriLauncher uriLauncher,
        IFilePicker filePicker,
        StringLocalizer localizer,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            async () =>
            {
                services.GetRequiredService<IDiscordPresence>().Initialize();
                await InitializeCoreAsync(services, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        var created = await Task.Run(
            () => CreateMainWindowViewModel(
                services,
                settings,
                uriLauncher,
                filePicker,
                localizer),
            cancellationToken);

        var viewModel = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            created.BeginStartupLoading();
            mainWindow.DataContext = created;
            _mainWindowViewModel = created;
            return created;
        });

        var minimumVisibleTime = Task.Delay(TimeSpan.FromMilliseconds(950), cancellationToken);
        try
        {
            await Task.WhenAll(
                PreloadDynamicContentAsync(viewModel, cancellationToken),
                minimumVisibleTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(viewModel.CompleteStartupLoading);
    }

    private static MainWindowViewModel CreateMainWindowViewModel(
        IServiceProvider services,
        IDesktopSettingsStore settings,
        IExternalUriLauncher uriLauncher,
        IFilePicker filePicker,
        StringLocalizer localizer)
        => new(
            services.GetRequiredService<IInstanceRepository>(),
            services.GetRequiredService<IProfileManager>(),
            services.GetRequiredService<IProfileRepository>(),
            services.GetRequiredService<IGameLaunchCoordinator>(),
            services.GetRequiredService<IGameInstallationWorkflow>(),
            services.GetRequiredService<IGameProcessTracker>(),
            services.GetRequiredService<IProgressReporter>(),
            settings,
            services.GetRequiredService<IHytaleNewsClient>(),
            uriLauncher,
            services.GetRequiredService<HttpClient>(),
            localizer,
            filePicker,
            services.GetRequiredService<IGitHubClient>(),
            services.GetRequiredService<IMirrorCatalog>(),
            services.GetRequiredService<IMirrorDiscovery>(),
            services.GetRequiredService<IGameVersionCatalog>(),
            services.GetRequiredService<IModManager>(),
            services.GetRequiredService<IHytaleAuthenticator>(),
            services.GetRequiredService<RemoteImageCache>(),
            services.GetRequiredService<IGameConsoleService>(),
            services.GetRequiredService<IGpuProvider>(),
            services.GetRequiredService<LogSessionPaths>());

    private static async Task InitializeCoreAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            await Bootstrapper.InitializeAsync(services, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error("Bootstrapper", $"Asynchronous initialization failed: {exception}");
        }
    }

    private static async Task PreloadDynamicContentAsync(
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await viewModel.PreloadStartupDataAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning("Startup", "Dynamic content preload exceeded the startup time limit");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning("Startup", $"Dynamic content preload failed: {exception.Message}");
        }
    }
}
