// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hyprism.Desktop.Controls;

namespace Hyprism.Desktop.Screens.Profiles;

/// <summary>
/// Hosts the responsive master-detail layout for the profile manager.
/// </summary>
public sealed partial class ProfilesView : UserControl
{
    private static readonly TimeSpan CompactContentTransitionDuration = MotionDurations.CompactPageSlide;

    private readonly WizardHost _creatorWizard;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private readonly ReorderableListController _profileReorder;
    private INotifyPropertyChanged? _viewModel;
    private bool _creatorOpenedFromCompactList;
    private bool _creatorTransitionActive;
    private bool _isCreatorVisible;
    private int _creatorNavigationRevision;

    public ProfilesView()
    {
        InitializeComponent();
        _creatorWizard = new WizardHost(
            ProfileOverview,
            ProfileCreatorScreen,
            ProfilesListPane,
            ProfileWizardReveal,
            new WizardStepDefinition(
                ProfileCreationChoiceContent,
                "/Assets/Lotties/avatar-reveal.json"),
            new WizardStepDefinition(
                OfflineProfileCreationContent,
                "/Assets/Lotties/avatar-jumping.json"),
            new WizardStepDefinition(
                OfficialProfileCreationContent,
                "/Assets/Lotties/avatar-looking.json"));
        _creatorWizard.ConfigureNavigation(
            () => DataContext is ProfilesViewModel { IsCreationVisible: true },
            () => (DataContext as ProfilesViewModel)?.CancelCreationCommand.Execute(null));
        _creatorWizard.RegisterPreviousStep(
            OfflineProfileCreationContent,
            ProfileCreationChoiceContent,
            () => (DataContext as ProfilesViewModel)?.ReturnToCreationChoiceCommand.Execute(null));
        _creatorWizard.RegisterPreviousStep(
            OfficialProfileCreationContent,
            ProfileCreationChoiceContent,
            () => (DataContext as ProfilesViewModel)?.ReturnToCreationChoiceCommand.Execute(null));
        _layoutHost = new AdaptiveMasterDetailHost(
            ProfilesLayout,
            ProfilesListPane,
            ProfileMain,
            CompactProfilesToolbar,
            ProfilesContentHost,
            compact =>
            {
                Classes.Set("compact", compact);
                Classes.Set("wide", !compact);
            });
        _profileReorder = new ReorderableListController(
            ProfilesItems,
            ProfilesLayout,
            ProfileDragPreview,
            ProfilesListPane);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnProfileDeleteModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is ProfilesViewModel viewModel)
            viewModel.CompleteProfileDeletionClose();
    }

    private void OnProfileEditModalClosed(object? sender, EventArgs args)
    {
        if (DataContext is ProfilesViewModel viewModel)
            viewModel.CompleteProfileEditClose();
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateLayout(Bounds.Width);
        _isCreatorVisible = DataContext is ProfilesViewModel { IsCreationVisible: true };
        if (_isCreatorVisible)
            _ = PlayCreatorOpenAnimationAsync();
        else
            HideCreatorImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProfilesViewModel.HasProfiles))
            UpdateLayout(Bounds.Width);

        if (args.PropertyName is nameof(ProfilesViewModel.IsCreationVisible))
        {
            var isCreatorVisible = DataContext is ProfilesViewModel { IsCreationVisible: true };
            if (_isCreatorVisible == isCreatorVisible)
                return;

            _isCreatorVisible = isCreatorVisible;
            if (isCreatorVisible)
                _ = PlayCreatorOpenAnimationAsync();
            else
                _ = PlayCreatorCloseAnimationAsync();
        }
    }

    private void OnProfilesViewSizeChanged(object? sender, SizeChangedEventArgs args)
        => UpdateLayout(args.NewSize.Width);

    private void UpdateLayout(double width)
    {
        if (width <= 0 || DataContext is not ProfilesViewModel viewModel)
            return;

        var wasCompact = _layoutHost.IsCompact;
        if (viewModel.IsCreationVisible && !wasCompact &&
            width < AdaptiveMasterDetailHost.DefaultBreakpoint)
            _layoutHost.RememberDetail();
        _layoutHost.Update(width, viewModel.HasProfiles, viewModel.IsProfileEditorVisible);
        if (wasCompact != _layoutHost.IsCompact)
        {
            ++_creatorNavigationRevision;
            _creatorTransitionActive = false;
            _creatorWizard.SyncLayout(viewModel.IsCreationVisible);
            _creatorOpenedFromCompactList = false;
            if (!viewModel.IsCreationVisible)
                viewModel.CompleteCreationTransition();
        }

        if (!viewModel.HasProfiles)
        {
            if (!_creatorTransitionActive)
                _creatorWizard.ResetNavigationPane();
            return;
        }

        if (!_layoutHost.IsCompact)
        {
            if (_creatorTransitionActive)
                return;

            if (viewModel.IsCreationVisible)
                _creatorWizard.HideNavigationPane(animate: false);
            else
                _creatorWizard.ShowNavigationPane(animate: false);
            return;
        }

        if (!_creatorTransitionActive)
            _creatorWizard.ResetNavigationPane();
    }

    private void OnProfileSelectedClicked(object? sender, RoutedEventArgs args)
    {
        _layoutHost.RememberDetail();
        if (_layoutHost.IsCompact)
            _layoutHost.OpenDetail();
    }

    private void OnCreateProfileClicked(object? sender, RoutedEventArgs args)
    {
        _creatorOpenedFromCompactList = _layoutHost.IsOpeningWizardFromMaster();
        if (_layoutHost.IsCompact && !_creatorOpenedFromCompactList)
            _layoutHost.OpenDetail();

        if (DataContext is ProfilesViewModel viewModel)
            viewModel.ShowCreateChoiceCommand.Execute(null);
    }

    private async void OnBeginOfficialProfileCreationClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel viewModel)
            return;

        await _creatorWizard.SwitchStepAsync(
            ProfileCreationChoiceContent,
            OfficialProfileCreationContent,
            forward: true,
            () => viewModel.BeginOfficialCreationCommand.Execute(null),
            () => viewModel.IsCreationVisible);
    }

    private async void OnBeginOfflineProfileCreationClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel viewModel)
            return;

        await _creatorWizard.SwitchStepAsync(
            ProfileCreationChoiceContent,
            OfflineProfileCreationContent,
            forward: true,
            () => viewModel.BeginOfflineCreationCommand.Execute(null),
            () => viewModel.IsCreationVisible);
    }

    private async void OnReturnToProfileCreationChoiceClicked(object? sender, RoutedEventArgs args)
        => await _creatorWizard.NavigateBackAsync();

    private void OnAuthenticationActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is ProfilesViewModel viewModel)
            viewModel.ArmAuthenticationCancellation();
    }

    private void OnCompactProfilesBackClicked(object? sender, RoutedEventArgs args)
        => TryCloseCompactContent();

    private static void OnProfileMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private void OnToggleProfileMenuPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: ProfileItemViewModel profile })
            profile.IsMenuOpen = !profile.IsMenuOpen;

        args.Handled = true;
    }

    private void OnCloseProfileMenuClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: ProfileItemViewModel profile })
            profile.IsMenuOpen = false;
    }

    private void OnProfileDragHandlePressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Border { DataContext: ProfileItemViewModel profile } handle)
            return;

        _profileReorder.Begin(handle, profile.Id, args, 72, () =>
        {
            ProfileDragPreviewName.Text = profile.Name;
            ProfileDragPreviewType.Text = profile.AccountType;
        });
    }

    private void OnProfileDragHandleMoved(object? sender, PointerEventArgs args)
        => _profileReorder.Move(args);

    private void OnProfileDragHandleReleased(object? sender, PointerReleasedEventArgs args)
    {
        _profileReorder.Complete(
            args,
            (profileId, targetIndex) =>
            {
                if (DataContext is ProfilesViewModel viewModel)
                    viewModel.MoveProfile(profileId, targetIndex);
            },
            () =>
            {
                ProfileDragPreviewName.Text = string.Empty;
                ProfileDragPreviewType.Text = string.Empty;
            });
    }

    private async void OnCopyUuidClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel { SelectedProfile: { } profile })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is not null)
            await topLevel.Clipboard.SetTextAsync(profile.Uuid);
    }

    private void OnCloseCompactProfileMenuClicked(object? sender, RoutedEventArgs args)
        => CompactProfileMenuPopup.IsRequestedOpen = false;

    private void OnToggleCompactProfileMenuClicked(object? sender, RoutedEventArgs args)
        => CompactProfileMenuPopup.IsRequestedOpen = !CompactProfileMenuPopup.IsRequestedOpen;

    public bool TryCloseCompactContent()
    {
        if (_creatorWizard.TryNavigateBack())
            return true;

        if (!_layoutHost.IsCompact || !_layoutHost.IsDetailOpen)
            return false;

        return _layoutHost.TryCloseDetail();
    }

    private async Task PlayCreatorOpenAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        _creatorTransitionActive = true;
        try
        {
            if (_creatorOpenedFromCompactList && _layoutHost.IsCompact)
            {
                await _creatorWizard.ShowWizardForCompactEntryAsync();
                if (revision != _creatorNavigationRevision ||
                    DataContext is not ProfilesViewModel { IsCreationVisible: true })
                {
                    return;
                }

                _layoutHost.OpenDetail();
                await Task.Delay(CompactContentTransitionDuration);
                return;
            }

            if (!_layoutHost.IsCompact &&
                DataContext is ProfilesViewModel { HasProfiles: true })
            {
                _creatorWizard.HideNavigationPane(animate: true);
            }

            await _creatorWizard.OpenAsync(
                () => DataContext is ProfilesViewModel { IsCreationVisible: true });
        }
        finally
        {
            if (revision == _creatorNavigationRevision)
            {
                _creatorTransitionActive = false;
                UpdateLayout(Bounds.Width);
            }
        }
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        _creatorTransitionActive = true;
        try
        {
            if (_creatorOpenedFromCompactList && _layoutHost.IsCompact)
            {
                _creatorWizard.Cancel();
                _layoutHost.TryCloseDetail();
                await Task.Delay(CompactContentTransitionDuration);
                if (revision == _creatorNavigationRevision &&
                    DataContext is ProfilesViewModel { IsCreationVisible: false } viewModel)
                {
                    _creatorWizard.ShowOverviewImmediately();
                    CompleteCreatorClose(viewModel);
                }

                return;
            }

            await _creatorWizard.CloseAsync(
                () => DataContext is ProfilesViewModel { IsCreationVisible: false },
                () =>
                {
                    if (DataContext is ProfilesViewModel viewModel)
                    {
                        if (!_layoutHost.IsCompact && viewModel.HasProfiles)
                            _creatorWizard.ShowNavigationPane(animate: true);

                        CompleteCreatorClose(viewModel);
                    }
                });
        }
        finally
        {
            if (revision == _creatorNavigationRevision)
            {
                _creatorTransitionActive = false;
                if (_layoutHost.IsCompact)
                    UpdateLayout(Bounds.Width);
            }
        }
    }

    private void HideCreatorImmediately()
    {
        ++_creatorNavigationRevision;
        _creatorTransitionActive = false;
        _creatorOpenedFromCompactList = false;
        _creatorWizard.ShowOverviewImmediately();
        if (!_layoutHost.IsCompact &&
            DataContext is ProfilesViewModel { HasProfiles: true })
        {
            _creatorWizard.ShowNavigationPane(animate: false);
        }
    }

    private void CompleteCreatorClose(ProfilesViewModel viewModel)
    {
        _creatorOpenedFromCompactList = false;
        viewModel.CompleteCreationTransition();
    }
}
