// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hyprism.Desktop.Controls;

/// <summary>
/// Coordinates the shared wide and compact layout used by manager-style pages.
/// </summary>
public sealed class AdaptiveMasterDetailHost
{
    public const double DefaultBreakpoint = ManagerLayoutMetrics.CompactBreakpoint;
    public const double DefaultContentMaxWidth = 720;

    private readonly Grid _layout;
    private readonly Control _master;
    private readonly Control _detail;
    private readonly Control? _compactToolbar;
    private readonly Control? _contentHost;
    private readonly Action<bool>? _applyCompactClass;
    private readonly double _compactBreakpoint;
    private double _width;
    private bool _hasMaster = true;
    private bool _showCompactToolbar = true;
    private bool _returnToDetail;

    public AdaptiveMasterDetailHost(
        Grid layout,
        Control master,
        Control detail,
        Control? compactToolbar = null,
        Control? contentHost = null,
        Action<bool>? applyCompactClass = null,
        double compactBreakpoint = DefaultBreakpoint)
    {
        if (compactBreakpoint <= 0)
            throw new ArgumentOutOfRangeException(nameof(compactBreakpoint));

        _layout = layout;
        _master = master;
        _detail = detail;
        _compactToolbar = compactToolbar;
        _contentHost = contentHost;
        _applyCompactClass = applyCompactClass;
        _compactBreakpoint = compactBreakpoint;
        _detail.RenderTransform ??= new TranslateTransform();
    }

    public bool IsCompact { get; private set; }
    public bool IsDetailOpen { get; private set; }

    public void Update(double width, bool hasMaster, bool showCompactToolbar = true)
    {
        if (width <= 0)
            return;

        _width = width;
        _hasMaster = hasMaster;
        _showCompactToolbar = showCompactToolbar;
        var compact = width < _compactBreakpoint;
        if (IsCompact != compact)
        {
            if (compact)
                IsDetailOpen = _returnToDetail;
            else if (IsCompact)
                _returnToDetail = IsDetailOpen;

            IsCompact = compact;
        }

        Apply(animateDetail: false);
    }

    public void RememberDetail()
        => _returnToDetail = true;

    public void OpenDetail()
    {
        _returnToDetail = true;
        IsDetailOpen = true;
        Apply(animateDetail: IsCompact);
    }

    public bool TryCloseDetail()
    {
        if (!IsCompact || !IsDetailOpen)
            return false;

        _returnToDetail = false;
        IsDetailOpen = false;
        Apply(animateDetail: true);
        return true;
    }

    public bool IsOpeningWizardFromMaster()
        => IsCompact && !IsDetailOpen && _hasMaster;

    public void SetDetailOffsetWithoutTransition(double offset)
    {
        var translation = GetTranslation(_detail);
        var transitions = translation.Transitions;
        translation.Transitions = null;
        translation.X = offset;
        translation.Transitions = transitions;
    }

    private void Apply(bool animateDetail)
    {
        if (_width <= 0)
            return;

        _applyCompactClass?.Invoke(IsCompact);
        if (_contentHost is not null)
        {
            _contentHost.Margin = IsCompact
                ? new Thickness(24, 16, 24, 36)
                : new Thickness(32, 28, 32, 40);
            _contentHost.MaxWidth = IsCompact
                ? double.PositiveInfinity
                : DefaultContentMaxWidth;
        }

        if (!_hasMaster)
        {
            _layout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _layout.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(_detail, 0);
            Grid.SetColumnSpan(_detail, 2);
            _master.IsHitTestVisible = false;
            _detail.IsHitTestVisible = true;
            if (_compactToolbar is not null)
                _compactToolbar.IsVisible = false;
            SetDetailOffsetWithoutTransition(0);
            return;
        }

        if (!IsCompact)
        {
            _layout.ColumnDefinitions[0].Width = GridLength.Auto;
            _layout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(_master, 0);
            Grid.SetColumn(_detail, 1);
            Grid.SetColumnSpan(_detail, 1);
            _master.IsHitTestVisible = true;
            _detail.IsHitTestVisible = true;
            if (_compactToolbar is not null)
                _compactToolbar.IsVisible = false;
            SetDetailOffsetWithoutTransition(0);
            return;
        }

        _layout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        _layout.ColumnDefinitions[1].Width = new GridLength(0);
        Grid.SetColumn(_master, 0);
        Grid.SetColumn(_detail, 0);
        Grid.SetColumnSpan(_detail, 2);
        _master.IsHitTestVisible = !IsDetailOpen;
        _detail.IsHitTestVisible = IsDetailOpen;
        if (_compactToolbar is not null)
            _compactToolbar.IsVisible = _showCompactToolbar;
        SetDetailOffset(IsDetailOpen ? 0 : _width, animateDetail);
    }

    private void SetDetailOffset(double offset, bool animate)
    {
        if (animate)
            GetTranslation(_detail).X = offset;
        else
            SetDetailOffsetWithoutTransition(offset);
    }

    private static TranslateTransform GetTranslation(Control control)
        => (TranslateTransform)control.RenderTransform!;
}
