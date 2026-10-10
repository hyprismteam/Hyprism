// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Threading;
using Avalonia.Media.Imaging;
using System.Globalization;

namespace Hyprism.Desktop.Screens.Instances;

public sealed partial class InstancesViewModel
{
    private readonly DispatcherTimer _worldsRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };
    private FileSystemWatcher? _worldsWatcher;
    private CancellationTokenSource? _worldsReadCancellation;
    private long _worldsReadVersion;
    private bool _worldsRefreshPending;

    private void ClearInstanceWorlds()
    {
        foreach (var world in InstanceWorlds)
            world.Dispose();
        InstanceWorlds.Clear();
    }

    private static void DisposeUnusedWorlds(
        IEnumerable<InstanceWorldItemViewModel> worlds, IReadOnlyList<InstanceWorldItemViewModel> retained)
    {
        foreach (var world in worlds)
        {
            if (!retained.Any(item => ReferenceEquals(item, world)))
                world.Dispose();
        }
    }

    private void StartWorldsSynchronization()
    {
        if (_worldsWatcher is not null || Volatile.Read(ref _isDisposed) != 0)
            return;
        var path = _managedInstance?.IsInstalled == true
            ? _instances.GetInstancePathById(_managedInstance.Id)
            : null;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            // Watch the instance so creating UserData/Saves is observed too.
            _worldsWatcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _worldsWatcher.Created += OnWorldsFileChanged;
            _worldsWatcher.Changed += OnWorldsFileChanged;
            _worldsWatcher.Deleted += OnWorldsFileChanged;
            _worldsWatcher.Renamed += OnWorldsFileChanged;
            _worldsWatcher.Error += OnWorldsWatcherError;
            _worldsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _worldsWatcher?.Dispose();
            _worldsWatcher = null;
            if (IsInstanceWorldsSection)
                InstanceContentError = ex.Message;
        }
    }

    private void StopWorldsSynchronization()
    {
        _worldsWatcher?.Dispose();
        _worldsWatcher = null;
        _worldsRefreshTimer.Stop();
        _worldsReadCancellation?.Cancel();
        _worldsReadCancellation?.Dispose();
        _worldsReadCancellation = null;
        _worldsReadVersion++;
        _worldsRefreshPending = false;
        IsInstanceWorldsLoading = false;
    }

    private void OnWorldsFileChanged(object sender, FileSystemEventArgs args)
    {
        if (sender is not FileSystemWatcher watcher)
            return;

        bool AffectsSaves(string path)
        {
            var relative = Path.GetRelativePath(watcher.Path, path);
            return relative == "UserData" || relative == Path.Combine("UserData", "Saves") ||
                   relative.StartsWith(Path.Combine("UserData", "Saves") + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        if (AffectsSaves(args.FullPath) || args is RenamedEventArgs renamed && AffectsSaves(renamed.OldFullPath))
            QueueWorldsRefresh(watcher);
    }

    private void OnWorldsWatcherError(object sender, ErrorEventArgs args)
        => QueueWorldsRefresh(sender, restartWatcher: true);

    private void QueueWorldsRefresh(object watcher, bool restartWatcher = false)
        => Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _isDisposed) != 0 || !ReferenceEquals(watcher, _worldsWatcher))
                return;

            if (restartWatcher)
            {
                StopWorldsSynchronization();
                StartWorldsSynchronization();
            }
            _worldsRefreshTimer.Stop();
            _worldsRefreshTimer.Start();
        });

    private void OnWorldsRefreshTimerTick(object? sender, EventArgs args)
    {
        _worldsRefreshTimer.Stop();
        _ = LoadInstanceWorldsAsync();
    }

    private async Task LoadInstanceWorldsAsync()
    {
        if (Volatile.Read(ref _isDisposed) != 0 || _managedInstance?.IsInstalled != true)
            return;
        StartWorldsSynchronization();
        if (IsInstanceWorldsLoading)
        {
            _worldsRefreshPending = true;
            return;
        }

        var instanceId = _managedInstance.Id;
        var instancePath = _instances.GetInstancePathById(instanceId);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        _worldsReadCancellation?.Dispose();
        _worldsReadCancellation = new CancellationTokenSource();
        var cancellationToken = _worldsReadCancellation.Token;
        var culture = CultureInfo.GetCultureInfo(_localizer.CurrentLanguage);
        var previousWorlds = InstanceWorlds.ToArray();
        var version = ++_worldsReadVersion;
        IsInstanceWorldsLoading = true;
        if (IsInstanceWorldsSection)
            InstanceContentError = string.Empty;
        try
        {
            var worlds = await Task.Run(
                () => ReadInstanceWorlds(instancePath, culture, previousWorlds, cancellationToken), cancellationToken);
            if (version != _worldsReadVersion || cancellationToken.IsCancellationRequested ||
                !string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
            {
                DisposeUnusedWorlds(worlds, previousWorlds);
                return;
            }

            if (!InstanceWorlds.SequenceEqual(worlds))
            {
                _instanceWorlds.ReplaceRange(worlds);
                DisposeUnusedWorlds(previousWorlds, worlds);
                OnPropertyChanged(nameof(InstanceWorldsCountText));
                OnPropertyChanged(nameof(HasInstanceWorlds));
                OnPropertyChanged(nameof(IsInstanceWorldsEmpty));
                OnPropertyChanged(nameof(IsInstanceWorldsInitialLoading));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (version == _worldsReadVersion && IsInstanceWorldsSection)
                InstanceContentError = ex.Message;
        }
        finally
        {
            if (version == _worldsReadVersion)
            {
                IsInstanceWorldsLoading = false;
                if (_worldsRefreshPending)
                {
                    _worldsRefreshPending = false;
                    _ = LoadInstanceWorldsAsync();
                }
            }
        }
    }

    private static IReadOnlyList<InstanceWorldItemViewModel> ReadInstanceWorlds(
        string instancePath, CultureInfo culture, IReadOnlyList<InstanceWorldItemViewModel> previousWorlds,
        CancellationToken cancellationToken)
    {
        var savesPath = Path.Combine(instancePath, "UserData", "Saves");
        if (!Directory.Exists(savesPath))
            return [];

        var worlds = new List<(InstanceWorldItemViewModel Item, DateTime Modified)>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        try
        {
            foreach (var path in Directory.EnumerateDirectories(savesPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var directory = new DirectoryInfo(path);
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    var modified = directory.LastWriteTimeUtc;
                    long size = 0;
                    foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (entry is FileInfo file)
                                size += file.Length;
                            if (entry.LastWriteTimeUtc > modified)
                                modified = entry.LastWriteTimeUtc;
                        }
                        catch (FileNotFoundException) { }
                    }
                    var previewFile = new FileInfo(Path.Combine(path, "preview.png"));
                    var hasPreviewFile = previewFile.Exists &&
                                         (previewFile.Attributes & FileAttributes.ReparsePoint) == 0;
                    var item = new InstanceWorldItemViewModel(directory.Name,
                        modified.ToLocalTime().ToString("g", culture), FormatBytes(size, culture),
                        PreviewPath: hasPreviewFile ? previewFile.FullName : null,
                        PreviewModifiedUtc: hasPreviewFile ? previewFile.LastWriteTimeUtc : default,
                        PreviewLength: hasPreviewFile ? previewFile.Length : 0);
                    var previous = previousWorlds.FirstOrDefault(world => world.Name == item.Name &&
                        world.LastModified == item.LastModified && world.Size == item.Size &&
                        world.PreviewPath == item.PreviewPath && world.PreviewModifiedUtc == item.PreviewModifiedUtc &&
                        world.PreviewLength == item.PreviewLength);
                    worlds.Add((previous ?? item with { Preview = LoadWorldPreview(item.PreviewPath) }, modified));
                }
                catch (DirectoryNotFoundException) { }
            }
            return worlds.OrderByDescending(world => world.Modified).Select(world => world.Item).ToArray();
        }
        catch
        {
            DisposeUnusedWorlds(worlds.Select(world => world.Item), previousWorlds);
            throw;
        }
    }

    private static Bitmap? LoadWorldPreview(string? path)
    {
        if (path is null)
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return DecodeInstanceIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
