// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Shell;

public sealed partial class OnboardingView : UserControl
{
    private readonly WizardHost _wizard;
    private bool _isNavigating;
    private OnboardingViewModel? _observedViewModel;

    public OnboardingView()
    {
        InitializeComponent();
        _wizard = new WizardHost(
            OnboardingOverview,
            OnboardingWizard,
            null,
            OnboardingLogoAnchor,
            OnboardingLogo,
            null,
            WelcomeContent,
            LanguageContent,
            AppearanceContent,
            AccountContent,
            OfficialContent,
            WarningContent,
            OfflineNameContent,
            FinishContent);
        _wizard.ConfigureNavigation(
            () => DataContext is OnboardingViewModel,
            () => _ = NavigateBackAsync());
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            ObserveViewModel(DataContext as OnboardingViewModel);
            _wizard.ShowWizardImmediately();
        };
        DetachedFromVisualTree += (_, _) => ObserveViewModel(null);
    }

    private Control StepControl(OnboardingStep step) => step switch
    {
        OnboardingStep.Welcome => WelcomeContent,
        OnboardingStep.Language => LanguageContent,
        OnboardingStep.Appearance => AppearanceContent,
        OnboardingStep.Account => AccountContent,
        OnboardingStep.Official => OfficialContent,
        OnboardingStep.DownloadWarning => WarningContent,
        OnboardingStep.OfflineName => OfflineNameContent,
        OnboardingStep.Finishing => FinishContent,
        _ => WelcomeContent
    };

    private void OnDataContextChanged(object? sender, EventArgs e)
        => ObserveViewModel(DataContext as OnboardingViewModel);

    private void ObserveViewModel(OnboardingViewModel? viewModel)
    {
        if (_observedViewModel is not null)
            _observedViewModel.CompletionRequested -= OnCompletionRequested;

        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
            _observedViewModel.CompletionRequested += OnCompletionRequested;
    }

    private void OnCompletionRequested() => _ = FinishAsync();

    private async Task FinishAsync()
    {
        if (DataContext is not OnboardingViewModel viewModel)
            return;

        await NavigateAsync(OnboardingStep.Finishing, true,
            () => viewModel.Step = OnboardingStep.Finishing);

        if (!ReferenceEquals(DataContext, viewModel))
            return;

        if (TopLevel.GetTopLevel(this) is MainWindow window)
            await window.RevealLauncherFromOnboardingAsync(viewModel);
        else
            viewModel.FinishCompletion();
    }

    private async Task NavigateAsync(OnboardingStep destination, bool forward, Action updateStep)
    {
        if (_isNavigating || DataContext is not OnboardingViewModel viewModel ||
            viewModel.Step == destination)
            return;

        _isNavigating = true;
        try
        {
            await _wizard.SwitchStepAsync(
                StepControl(viewModel.Step),
                StepControl(destination),
                forward,
                updateStep,
                () => ReferenceEquals(DataContext, viewModel));
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private async void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OnboardingViewModel viewModel)
            return;

        var next = viewModel.Step switch
        {
            OnboardingStep.Welcome => OnboardingStep.Language,
            OnboardingStep.Language => OnboardingStep.Appearance,
            OnboardingStep.Appearance => OnboardingStep.Account,
            _ => viewModel.Step
        };
        await NavigateAsync(next, true, () => viewModel.NextCommand.Execute(null));
    }

    private async void OnBackClicked(object? sender, RoutedEventArgs e)
        => await NavigateBackAsync();

    private Task NavigateBackAsync()
    {
        if (DataContext is not OnboardingViewModel viewModel || !viewModel.CanGoBack)
            return Task.CompletedTask;

        return NavigateAsync(viewModel.PreviousStep, false, () => viewModel.BackCommand.Execute(null));
    }

    private async void OnOfficialClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OnboardingViewModel viewModel)
            await NavigateAsync(OnboardingStep.Official, true,
                () => viewModel.ChooseOfficialCommand.Execute(null));
    }

    private async void OnOfflineClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OnboardingViewModel viewModel)
            return;

        var destination = viewModel.ShouldShowDownloadWarning
            ? OnboardingStep.DownloadWarning
            : OnboardingStep.OfflineName;
        await NavigateAsync(destination, true, () => viewModel.ChooseOfflineCommand.Execute(null));
    }

    private async void OnContinueOfflineClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OnboardingViewModel viewModel)
            await NavigateAsync(OnboardingStep.OfflineName, true,
                () => viewModel.ContinueOfflineCommand.Execute(null));
    }

    private void OnAuthenticationActionPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is OnboardingViewModel viewModel)
            viewModel.Profiles.ArmAuthenticationCancellation();
    }
}
