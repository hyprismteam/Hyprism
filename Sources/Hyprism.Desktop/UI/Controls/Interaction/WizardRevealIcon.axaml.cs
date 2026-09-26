// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Lottie;
using Avalonia.Styling;

namespace Hyprism.Desktop.Controls;

public sealed partial class WizardRevealIcon : Border
{
    public static readonly StyledProperty<string?> AnimationPathProperty =
        AvaloniaProperty.Register<WizardRevealIcon, string?>(nameof(AnimationPath));
    public static readonly StyledProperty<double> PlaybackRateProperty =
        AvaloniaProperty.Register<WizardRevealIcon, double>(nameof(PlaybackRate), 2);

    public WizardRevealIcon()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateAnimationPath();
    }

    public string? AnimationPath
    {
        get => GetValue(AnimationPathProperty);
        set => SetValue(AnimationPathProperty, value);
    }

    public double PlaybackRate
    {
        get => GetValue(PlaybackRateProperty);
        set => SetValue(PlaybackRateProperty, value);
    }

    public Control Anchor => this;
    public Border MotionTarget => AnimationMotionTarget;
    public Lottie Animation => AnimationPlayer;
    internal bool LastSelectionWasAnimated { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AnimationPathProperty && AnimationPlayer is not null)
            UpdateAnimationPath();
    }

    public void Play(string animationPath)
    {
        LastSelectionWasAnimated = true;
        AnimationPlayer.Stop();
        Select(animationPath);
        AnimationPlayer.SeekToProgress(0);
        AnimationPlayer.Start();
    }

    internal void ShowInitialFrame(string animationPath)
    {
        LastSelectionWasAnimated = false;
        var autoPlay = AnimationPlayer.AutoPlay;
        AnimationPlayer.AutoPlay = false;
        try
        {
            AnimationPlayer.Stop();
            Select(animationPath);
            AnimationPlayer.Start();
            AnimationPlayer.SeekToProgress(0);
            AnimationPlayer.Pause();
        }
        finally
        {
            AnimationPlayer.AutoPlay = autoPlay;
        }
    }

    public void ShowFinalFrame(string animationPath)
    {
        LastSelectionWasAnimated = false;
        var autoPlay = AnimationPlayer.AutoPlay;
        AnimationPlayer.AutoPlay = false;
        try
        {
            AnimationPlayer.Stop();
            Select(animationPath);
            AnimationPlayer.Start();
            AnimationPlayer.SeekToProgress(0.999f);
            AnimationPlayer.Pause();
        }
        finally
        {
            AnimationPlayer.AutoPlay = autoPlay;
        }
    }

    internal void Select(string animationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationPath);
        SetCurrentValue(AnimationPathProperty, animationPath);
    }

    private void UpdateAnimationPath()
    {
        if (string.IsNullOrEmpty(AnimationPath))
            return;

        AnimationPlayer.Path = ActualThemeVariant == ThemeVariant.Light && AnimationPath.EndsWith(".json", StringComparison.Ordinal)
            ? $"{AnimationPath[..^5]}.light.json"
            : AnimationPath;
    }
}
