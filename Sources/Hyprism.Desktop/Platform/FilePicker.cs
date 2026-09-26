// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Desktop.Platform;

/// <summary>
/// Presents native file and folder pickers through Avalonia's storage provider
/// </summary>
/// <param name="topLevelProvider">Resolves the active top-level window when a picker is requested</param>
public sealed class FilePicker(Func<TopLevel?> topLevelProvider) : IFilePicker
{
    private static readonly FilePickerFileType JavaFileType = new("Java executable")
    {
        Patterns = OperatingSystem.IsWindows()
            ? ["java.exe", "javaw.exe", "*.exe"]
            : ["java", "javaw", "*"]
    };

    private static readonly FilePickerFileType ModFileType = new("Hytale mod")
    {
        Patterns = ["*.jar", "*.zip"]
    };

    private static readonly FilePickerFileType InstanceArchiveFileType = new("Hyprism instance archive")
    {
        Patterns = ["*.zip", "*.pwr"]
    };

    private static readonly FilePickerFileType ImageFileType = new("Image")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg"]
    };

    /// <inheritdoc/>
    public Task<string?> BrowseFolderAsync(string? initialPath = null)
        => RunOnUiThreadAsync(async storageProvider =>
        {
            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select folder",
                AllowMultiple = false,
                SuggestedStartLocation = await ResolveFolderAsync(storageProvider, initialPath)
            });

            return folders.FirstOrDefault()?.TryGetLocalPath();
        });

    /// <inheritdoc/>
    public Task<string?> BrowseJavaExecutableAsync(string? initialDirectory = null)
        => RunOnUiThreadAsync(async storageProvider =>
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Java executable",
                AllowMultiple = false,
                FileTypeFilter = [JavaFileType],
                SuggestedStartLocation = await ResolveFolderAsync(storageProvider, initialDirectory)
            });

            return files.FirstOrDefault()?.TryGetLocalPath();
        });

    /// <inheritdoc/>
    public async Task<string[]> BrowseModFilesAsync()
    {
        var result = await RunOnUiThreadAsync(async storageProvider =>
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select mod files",
                AllowMultiple = true,
                FileTypeFilter = [ModFileType]
            });

            return files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();
        });

        return result ?? [];
    }

    /// <inheritdoc/>
    public Task<string?> SaveFileAsync(string defaultFileName, string filter, string? initialPath = null)
        => RunOnUiThreadAsync(async storageProvider =>
        {
            var fileType = CreateSaveFileType(filter);
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save file",
                SuggestedFileName = defaultFileName,
                DefaultExtension = GetFirstExtension(fileType),
                FileTypeChoices = [fileType],
                SuggestedStartLocation = await ResolveFolderAsync(storageProvider, initialPath)
            });

            return file?.TryGetLocalPath();
        });

    /// <inheritdoc/>
    public Task<string?> BrowseInstanceArchiveAsync()
        => PickSingleFileAsync("Select instance archive", [InstanceArchiveFileType]);

    /// <inheritdoc/>
    public Task<string?> BrowseImageAsync(string title)
        => PickSingleFileAsync(title, [ImageFileType]);

    private Task<string?> PickSingleFileAsync(string title, IReadOnlyList<FilePickerFileType> fileTypes)
        => RunOnUiThreadAsync(async storageProvider =>
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            });

            return files.FirstOrDefault()?.TryGetLocalPath();
        });

    private async Task<T?> RunOnUiThreadAsync<T>(Func<IStorageProvider, Task<T?>> action)
    {
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return await Dispatcher.UIThread.InvokeAsync(() => RunOnUiThreadAsync(action));
            }

            var topLevel = topLevelProvider();
            if (topLevel is null)
            {
                Logger.Warning("Files", "Cannot show a file picker before the main window is available");
                return default;
            }

            return await action(topLevel.StorageProvider);
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Native file picker failed: {ex.Message}");
            return default;
        }
    }

    private static async Task<IStorageFolder?> ResolveFolderAsync(
        IStorageProvider storageProvider,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        return await storageProvider.TryGetFolderFromPathAsync(path);
    }

    private static FilePickerFileType CreateSaveFileType(string filter)
    {
        var parts = filter.Split('|', 2, StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "File";
        var patterns = parts.Length > 1
            ? parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["*"];

        return new FilePickerFileType(name) { Patterns = patterns };
    }

    private static string? GetFirstExtension(FilePickerFileType fileType)
    {
        var pattern = fileType.Patterns?.FirstOrDefault();
        return pattern?.StartsWith("*.", StringComparison.Ordinal) == true ? pattern[2..] : null;
    }
}
