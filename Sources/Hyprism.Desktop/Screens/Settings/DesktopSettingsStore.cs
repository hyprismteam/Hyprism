// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using Hyprism.Core;
using Hyprism.Core.Infrastructure;

namespace Hyprism.Desktop.Screens.Settings;

/// <summary>
/// Persists Avalonia preferences through the shared configuration store
/// </summary>
public sealed class DesktopSettingsStore : IDesktopSettingsStore
{
    private const string LibraryDirectoryName = "HyprismLibrary";
    private const string LibraryMarkerName = ".hyprism-library";
    private readonly IConfigStore _configStore;
    private readonly string _appDirectory;

    public DesktopSettingsStore(IConfigStore configStore)
        : this(configStore, new AppPathConfiguration(LauncherUtilities.GetEffectiveAppDir()))
    {
    }

    public DesktopSettingsStore(IConfigStore configStore, AppPathConfiguration appPath)
    {
        _configStore = configStore;
        _appDirectory = Path.GetFullPath(appPath.AppDir);
    }

    /// <inheritdoc/>
    public string Language
    {
        get => _configStore.Configuration.Language;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            Save(config => config.Language = value);
        }
    }

    /// <inheritdoc/>
    public bool MusicEnabled
    {
        get => _configStore.Configuration.MusicEnabled;
        set => Save(config => config.MusicEnabled = value);
    }

    /// <inheritdoc/>
    public bool CloseAfterLaunch
    {
        get => _configStore.Configuration.CloseAfterLaunch;
        set => Save(config => config.CloseAfterLaunch = value);
    }

    /// <inheritdoc/>
    public bool ShowDiscordAnnouncements
    {
        get => _configStore.Configuration.ShowDiscordAnnouncements;
        set => Save(config => config.ShowDiscordAnnouncements = value);
    }

    /// <inheritdoc/>
    public bool DisableNews
    {
        get => _configStore.Configuration.DisableNews;
        set => Save(config => config.DisableNews = value);
    }

    /// <inheritdoc/>
    public bool OnlineMode
    {
        get => _configStore.Configuration.OnlineMode;
        set => Save(config => config.OnlineMode = value);
    }

    /// <inheritdoc/>
    public string AuthDomain
    {
        get => _configStore.Configuration.AuthDomain;
        set => Save(config => config.AuthDomain = string.IsNullOrWhiteSpace(value)
            ? "sessions.sanasol.ws"
            : value.Trim());
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> AuthServers
    {
        get => _configStore.Configuration.AuthServers;
        set => Save(config => config.AuthServers = value?
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Select(server => server.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? []);
    }

    /// <inheritdoc/>
    public string JavaArguments
    {
        get => _configStore.Configuration.JavaArguments;
        set => Save(config => config.JavaArguments = value?.Trim() ?? string.Empty);
    }

    /// <inheritdoc/>
    public bool UseCustomJava
    {
        get => _configStore.Configuration.UseCustomJava;
        set => Save(config => config.UseCustomJava = value);
    }

    /// <inheritdoc/>
    public string CustomJavaPath
    {
        get => _configStore.Configuration.CustomJavaPath;
        set => Save(config => config.CustomJavaPath = value?.Trim() ?? string.Empty);
    }

    /// <inheritdoc/>
    public string GpuPreference
    {
        get => _configStore.Configuration.GpuPreference;
        set
        {
            // Stored values are "auto", the legacy types, or an adapter key
            // ("pci:<id>" when the platform exposes it, otherwise the card name)
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                normalized = "dedicated";
            Save(config => config.GpuPreference = normalized);
        }
    }

    /// <inheritdoc/>
    public string GameEnvironmentVariables
    {
        get => _configStore.Configuration.GameEnvironmentVariables;
        set => Save(config => config.GameEnvironmentVariables = value ?? string.Empty);
    }

    /// <inheritdoc/>
    public string InstanceDirectory => _configStore.Configuration.InstanceDirectory;

    /// <inheritdoc/>
    public string DefaultInstanceDirectory => Path.Combine(_appDirectory, "Instances");

    /// <inheritdoc/>
    public string LauncherDataDirectory => _appDirectory;

    /// <inheritdoc/>
    public async Task<bool> SetInstanceDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default,
        IProgress<InstanceDirectoryMoveProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resetToDefault = string.IsNullOrWhiteSpace(path);
        var targetDirectory = resetToDefault
            ? DefaultInstanceDirectory
            : GetLibraryDirectory(NormalizeDirectory(path));
        var currentDirectory = string.IsNullOrWhiteSpace(InstanceDirectory)
            ? DefaultInstanceDirectory
            : NormalizeDirectory(InstanceDirectory);
        var ownedSource = DirectoriesEqual(currentDirectory, DefaultInstanceDirectory) ||
                          IsManagedLibrary(currentDirectory);

        if (DirectoriesEqual(currentDirectory, targetDirectory))
        {
            if (resetToDefault && !string.IsNullOrWhiteSpace(InstanceDirectory))
                await _configStore.SetInstanceDirectoryAsync(string.Empty);
            return true;
        }

        if ((ownedSource && IsNestedDirectory(currentDirectory, targetDirectory)) ||
            IsNestedDirectory(targetDirectory, currentDirectory))
        {
            Logger.Warning("Settings", "Instance storage cannot be moved into its current directory tree");
            return false;
        }

        try
        {
            if (!resetToDefault && !PrepareLibraryDirectory(targetDirectory))
                return false;

            await Task.Run(
                () => CopyDirectoryAsync(
                    currentDirectory,
                    targetDirectory,
                    cancellationToken,
                    progress,
                    ownedSource),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var saved = resetToDefault
                ? await SaveDefaultInstanceDirectoryAsync()
                : await _configStore.SetInstanceDirectoryAsync(targetDirectory) is not null;
            if (!saved)
                return false;

            if (ownedSource)
                TryRemovePreviousDirectory(currentDirectory);
            return true;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Logger.Error("Settings", $"Failed to move instance storage: {exception.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<LauncherStorageUsage> GetLauncherStorageUsageAsync(CancellationToken cancellationToken = default)
        => LauncherStorageUsageAnalyzer.MeasureAsync(
            LauncherDataDirectory,
            string.IsNullOrWhiteSpace(InstanceDirectory)
                ? DefaultInstanceDirectory
                : NormalizeDirectory(InstanceDirectory),
            cancellationToken);

    /// <inheritdoc/>
    public bool ShowAlphaMods
    {
        get => _configStore.Configuration.ShowAlphaMods;
        set => Save(config => config.ShowAlphaMods = value);
    }

    private void Save(Action<Hyprism.Core.Models.Config> update)
    {
        update(_configStore.Configuration);
        _configStore.SaveConfig();
    }

    private async Task<bool> SaveDefaultInstanceDirectoryAsync()
    {
        await _configStore.SetInstanceDirectoryAsync(string.Empty);
        return string.IsNullOrWhiteSpace(_configStore.Configuration.InstanceDirectory);
    }

    private string NormalizeDirectory(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(_appDirectory, expanded));
    }

    private static string GetLibraryDirectory(string selectedDirectory)
        => string.Equals(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(selectedDirectory)),
            LibraryDirectoryName,
            PathComparison)
            ? selectedDirectory
            : Path.Combine(selectedDirectory, LibraryDirectoryName);

    private static bool IsManagedLibrary(string directory)
        => string.Equals(
               Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)),
               LibraryDirectoryName,
               PathComparison) &&
           Directory.Exists(directory) &&
           !File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint) &&
           File.Exists(Path.Combine(directory, LibraryMarkerName)) &&
           !File.GetAttributes(Path.Combine(directory, LibraryMarkerName))
               .HasFlag(FileAttributes.ReparsePoint);

    private static bool PrepareLibraryDirectory(string directory)
    {
        if (IsManagedLibrary(directory))
            return true;

        if (Directory.Exists(directory) &&
            Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Logger.Warning("Settings", "The selected HyprismLibrary folder already contains unmanaged files");
            return false;
        }

        Directory.CreateDirectory(directory);
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The HyprismLibrary folder cannot be a symbolic link");
        File.WriteAllText(Path.Combine(directory, LibraryMarkerName), string.Empty);
        return true;
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken,
        IProgress<InstanceDirectoryMoveProgress>? progress,
        bool ownedSource)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDirectory);
        if (!Directory.Exists(sourceDirectory))
            return;

        var files = (ownedSource
                ? Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                : EnumerateLegacyInstanceFiles(sourceDirectory))
            .Select(path => new FileInfo(path))
            .ToArray();
        var totalBytes = files.Sum(file => file.Length);
        var copiedBytes = 0L;
        var lastReportedPercentage = -1;
        ReportProgress();

        foreach (var directory in ownedSource
                     ? Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories)
                     : Enumerable.Empty<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, file.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(
                file.FullName,
                destination,
                cancellationToken,
                copiedChunkBytes =>
                {
                    copiedBytes += copiedChunkBytes;
                    ReportProgress();
                });
        }

        copiedBytes = totalBytes;
        ReportProgress();

        void ReportProgress()
        {
            if (progress is null)
                return;

            var moveProgress = new InstanceDirectoryMoveProgress(copiedBytes, totalBytes);
            if (moveProgress.Percentage == lastReportedPercentage)
                return;

            lastReportedPercentage = moveProgress.Percentage;
            progress.Report(moveProgress);
        }
    }

    private static IEnumerable<string> EnumerateLegacyInstanceFiles(string directory)
    {
        foreach (var name in new[] { "Instances.json", "instances.json" })
        {
            var cache = Path.Combine(directory, name);
            if (File.Exists(cache))
                yield return cache;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                continue;

            var name = Path.GetFileName(child);
            if (Guid.TryParse(name, out _) && HasInstanceMetadata(child))
            {
                foreach (var file in EnumerateInstanceFiles(child))
                    yield return file;
            }
            else if (name.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var instance in Directory.EnumerateDirectories(child))
                {
                    if (File.GetAttributes(instance).HasFlag(FileAttributes.ReparsePoint) ||
                        !HasInstanceMetadata(instance))
                        continue;
                    foreach (var file in EnumerateInstanceFiles(instance))
                        yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateInstanceFiles(string directory)
        => Directory.EnumerateFiles(directory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        });

    private static bool HasInstanceMetadata(string directory)
        => File.Exists(Path.Combine(directory, "Meta.json")) ||
           File.Exists(Path.Combine(directory, "meta.json")) ||
           File.Exists(Path.Combine(directory, "metadata.json"));

    internal static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken,
        Action<int> reportBytesCopied)
    {
        var destinationDirectory = Path.GetDirectoryName(destination)!;
        var temporaryFile = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.hyprism-copy");

        try
        {
            await using (var sourceStream = new FileStream(
                             source,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destinationStream = new FileStream(
                             temporaryFile,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    while (true)
                    {
                        var bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);
                        if (bytesRead == 0)
                            break;

                        await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        reportBytesCopied(bytesRead);
                    }

                    await destinationStream.FlushAsync(cancellationToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryFile, destination, overwrite: true);
            PreserveUnixFileMode(source, destination);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch (Exception exception)
                {
                    Logger.Warning(
                        "Settings",
                        $"Temporary instance copy could not be removed: {exception.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Copies the Unix file mode from the source, because freshly written files
    /// lose the executable bit that game clients need to start
    /// </summary>
    private static void PreserveUnixFileMode(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
        catch (Exception exception)
        {
            Logger.Warning(
                "Settings",
                $"Failed to preserve the file mode while copying '{source}': {exception.Message}");
        }
    }

    private static void TryRemovePreviousDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", $"Instance storage moved, but the old directory could not be removed: {exception.Message}");
        }
    }

    private static bool IsNestedDirectory(string parent, string candidate)
    {
        var parentWithSeparator = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parentWithSeparator, PathComparison);
    }

    private static bool DirectoriesEqual(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

}
