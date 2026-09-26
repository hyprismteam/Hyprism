// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace Hyprism.Desktop.Tests;

internal static class AvaloniaTestWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    public static Task UntilAsync(Func<bool> condition)
        => UntilAsync(condition, "condition");

    public static async Task UntilAsync(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!condition() && Stopwatch.GetElapsedTime(startedAt) < (timeout ?? DefaultTimeout))
        {
            Pump();
            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }

        Pump();
        Assert.True(condition(), $"Timed out waiting for {description}");
    }

    public static async Task PropertyAsync(
        AvaloniaObject source,
        AvaloniaProperty property,
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        if (condition())
            return;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == property && condition())
                completion.TrySetResult(true);
        }

        source.PropertyChanged += OnPropertyChanged;
        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            while (!condition() &&
                   !completion.Task.IsCompleted &&
                   Stopwatch.GetElapsedTime(startedAt) < (timeout ?? DefaultTimeout))
            {
                Pump();
                await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
            }

            Pump();
            Assert.True(completion.Task.IsCompleted || condition(), $"Timed out waiting for {description}");
        }
        finally
        {
            source.PropertyChanged -= OnPropertyChanged;
        }
    }

    private static void Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }
}
