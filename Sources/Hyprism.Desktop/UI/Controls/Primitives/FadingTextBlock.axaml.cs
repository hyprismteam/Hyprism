// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Hyprism.Desktop.Controls;

public sealed record FadingTextState(string? ContextKey, string Text);

public sealed partial class FadingTextBlock : UserControl
{
    private static readonly TimeSpan TransitionDuration = MotionDurations.TextReplacement;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<FadingTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<FadingTextState?> StateProperty =
        AvaloniaProperty.Register<FadingTextBlock, FadingTextState?>(nameof(State));

    private CancellationTokenSource? _transitionCancellation;
    private string? _currentContextKey;
    private TranslateTransform CurrentTranslation => (TranslateTransform)CurrentText.RenderTransform!;
    private TranslateTransform IncomingTranslation => (TranslateTransform)IncomingText.RenderTransform!;

    public FadingTextBlock()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FadingTextState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            var state = change.GetNewValue<FadingTextState?>();
            var stateText = state?.Text ?? string.Empty;
            if (_currentContextKey != state?.ContextKey || CurrentText.Text is null)
                ShowImmediately(stateText);
            else if (CurrentText.Text != stateText)
                AnimateTo(stateText);
            _currentContextKey = state?.ContextKey;
            return;
        }

        if (change.Property != TextProperty)
            return;

        var text = change.GetNewValue<string?>() ?? string.Empty;
        if (CurrentText.Text is null)
        {
            CurrentText.Text = text;
            IncomingText.Text = text;
            return;
        }

        AnimateTo(text);
    }

    private void AnimateTo(string text)
    {
        _transitionCancellation?.Cancel();
        _transitionCancellation?.Dispose();
        _transitionCancellation = new CancellationTokenSource();
        _ = TransitionToAsync(text, _transitionCancellation.Token);
    }

    private void ShowImmediately(string text)
    {
        _transitionCancellation?.Cancel();
        _transitionCancellation?.Dispose();
        _transitionCancellation = null;
        CurrentText.Text = text;
        IncomingText.Text = text;
        SetWithoutTransitions(CurrentText, CurrentTranslation, opacity: 1, translationY: 0);
        SetWithoutTransitions(IncomingText, IncomingTranslation, opacity: 0, translationY: 10);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _transitionCancellation?.Cancel();
        _transitionCancellation?.Dispose();
        _transitionCancellation = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async Task TransitionToAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            IncomingText.Text = text;
            SetWithoutTransitions(IncomingText, IncomingTranslation, opacity: 0, translationY: 10);

            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            cancellationToken.ThrowIfCancellationRequested();
            CurrentText.Opacity = 0;
            CurrentTranslation.Y = -10;
            IncomingText.Opacity = 1;
            IncomingTranslation.Y = 0;

            await Task.Delay(TransitionDuration, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            CurrentText.Text = text;
            SetWithoutTransitions(CurrentText, CurrentTranslation, opacity: 1, translationY: 0);
            SetWithoutTransitions(IncomingText, IncomingTranslation, opacity: 0, translationY: 10);
        }
        catch (OperationCanceledException)
        {
            // A newer label replaces the pending cross-fade
        }
    }

    private static void SetWithoutTransitions(
        Control text,
        TranslateTransform translation,
        double opacity,
        double translationY)
    {
        var textTransitions = text.Transitions;
        var translationTransitions = translation.Transitions;
        text.Transitions = null;
        translation.Transitions = null;
        text.Opacity = opacity;
        translation.Y = translationY;
        text.Transitions = textTransitions;
        translation.Transitions = translationTransitions;
    }
}
