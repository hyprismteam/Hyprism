// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Moq;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class JavaCustomPathSettingsViewModelTests
{
    private const string LauncherDataDirectory = "C:\\Hyprism";
    private const string DefaultInstanceDirectory = "C:\\Hyprism\\Instances";

    [AvaloniaFact]
    public async Task BrowseJava_PersistsThePickedExecutableAndSwitchesToChange()
    {
        var settings = CreateSettingsStore();
        var picker = CreatePicker("/opt/jdk/bin/java", LauncherDataDirectory);
        using var viewModel = CreateViewModel(settings, picker);

        Assert.Equal("Edit", viewModel.EditCustomJavaPathLabel);
        Assert.False(viewModel.HasCustomJavaPath);
        Assert.Equal("No executable selected", viewModel.CustomJavaPathDisplay);

        await viewModel.BrowseJavaCommand.ExecuteAsync(null);

        Assert.Equal("/opt/jdk/bin/java", viewModel.CustomJavaPath);
        Assert.Equal("/opt/jdk/bin/java", settings.Object.CustomJavaPath);
        Assert.True(viewModel.UseCustomJava);
        Assert.True(viewModel.HasCustomJavaPath);
        Assert.Equal("Change", viewModel.EditCustomJavaPathLabel);
        Assert.Equal("/opt/jdk/bin/java", viewModel.CustomJavaPathDisplay);
    }

    [AvaloniaFact]
    public async Task BrowseJava_StartsInTheExecutableDirectoryWhenItStillExists()
    {
        var existingDirectory = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        var settings = CreateSettingsStore(Path.Combine(existingDirectory, "java"));
        var picker = CreatePicker(null, existingDirectory);
        using var viewModel = CreateViewModel(settings, picker);

        await viewModel.BrowseJavaCommand.ExecuteAsync(null);

        picker.Verify(
            service => service.BrowseJavaExecutableAsync(existingDirectory),
            Times.Once);
    }

    [AvaloniaFact]
    public async Task BrowseJava_FallsBackToTheLauncherDirectoryWhenTheCurrentDirectoryIsGone()
    {
        var settings = CreateSettingsStore("/gone/jdk/bin/java");
        var picker = CreatePicker(null, LauncherDataDirectory);
        using var viewModel = CreateViewModel(settings, picker);

        await viewModel.BrowseJavaCommand.ExecuteAsync(null);

        picker.Verify(
            service => service.BrowseJavaExecutableAsync(LauncherDataDirectory),
            Times.Once);
        Assert.Equal("/gone/jdk/bin/java", viewModel.CustomJavaPath);
    }

    [AvaloniaFact]
    public async Task MoveInstanceFolder_ClearsCustomJavaInsideTheOldInstanceTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "hyprism-move-tests");
        var configuredDirectory = Path.Combine(root, "Instances");
        var movedDirectory = Path.Combine(root, "Moved");
        var staleCustomJavaPath = Path.Combine(configuredDirectory, "jdk", "bin", "java");
        var settings = CreateSettingsStore(staleCustomJavaPath);
        settings.SetupGet(service => service.InstanceDirectory).Returns(() => configuredDirectory);
        settings
            .Setup(service => service.SetInstanceDirectoryAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()))
            .Callback<string, CancellationToken, IProgress<InstanceDirectoryMoveProgress>?>(
                (_, _, _) => configuredDirectory = movedDirectory)
            .ReturnsAsync(true);
        var picker = CreatePicker(null, LauncherDataDirectory);
        picker
            .Setup(service => service.BrowseFolderAsync(It.IsAny<string?>()))
            .ReturnsAsync(movedDirectory);
        using var viewModel = CreateViewModel(settings, picker);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.CustomJavaPath);
        Assert.Equal(string.Empty, settings.Object.CustomJavaPath);
        Assert.False(viewModel.HasCustomJavaPath);
        Assert.Equal("Edit", viewModel.EditCustomJavaPathLabel);
    }

    [AvaloniaFact]
    public async Task MoveInstanceFolder_KeepsCustomJavaOutsideTheInstanceTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "hyprism-move-tests");
        var configuredDirectory = Path.Combine(root, "Instances");
        var movedDirectory = Path.Combine(root, "Moved");
        var externalCustomJavaPath = Path.Combine(root, "Java", "jdk", "bin", "java");
        var settings = CreateSettingsStore(externalCustomJavaPath);
        settings.SetupGet(service => service.InstanceDirectory).Returns(() => configuredDirectory);
        settings
            .Setup(service => service.SetInstanceDirectoryAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()))
            .Callback<string, CancellationToken, IProgress<InstanceDirectoryMoveProgress>?>(
                (_, _, _) => configuredDirectory = movedDirectory)
            .ReturnsAsync(true);
        var picker = CreatePicker(null, LauncherDataDirectory);
        picker
            .Setup(service => service.BrowseFolderAsync(It.IsAny<string?>()))
            .ReturnsAsync(movedDirectory);
        using var viewModel = CreateViewModel(settings, picker);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);

        Assert.Equal(externalCustomJavaPath, viewModel.CustomJavaPath);
        Assert.Equal(externalCustomJavaPath, settings.Object.CustomJavaPath);
    }

    [AvaloniaFact]
    public async Task MoveInstanceFolder_KeepsCustomJavaInRetainedLegacyRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hyprism-legacy-java-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var configuredDirectory = root;
            var movedDirectory = Path.Combine(root, "HyprismLibrary");
            var customJavaPath = Path.Combine(root, "jdk", "bin", "java");
            var settings = CreateSettingsStore(customJavaPath);
            settings.SetupGet(service => service.InstanceDirectory).Returns(() => configuredDirectory);
            settings
                .Setup(service => service.SetInstanceDirectoryAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<InstanceDirectoryMoveProgress>?>()))
                .Callback<string, CancellationToken, IProgress<InstanceDirectoryMoveProgress>?>(
                    (_, _, _) => configuredDirectory = movedDirectory)
                .ReturnsAsync(true);
            var picker = CreatePicker(null, LauncherDataDirectory);
            picker
                .Setup(service => service.BrowseFolderAsync(It.IsAny<string?>()))
                .ReturnsAsync(root);
            using var viewModel = CreateViewModel(settings, picker);

            await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);

            Assert.Equal(customJavaPath, viewModel.CustomJavaPath);
            Assert.Equal(customJavaPath, settings.Object.CustomJavaPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SettingsViewModel CreateViewModel(
        Mock<IDesktopSettingsStore> settings,
        Mock<IFilePicker> picker)
        => new(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            picker.Object);

    private static Mock<IFilePicker> CreatePicker(string? pickedPath, string expectedInitialDirectory)
    {
        var picker = new Mock<IFilePicker>();
        picker
            .Setup(service => service.BrowseJavaExecutableAsync(expectedInitialDirectory))
            .ReturnsAsync(pickedPath);
        return picker;
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore(string customJavaPath = "")
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupProperty(service => service.JavaArguments, string.Empty);
        settings.SetupProperty(service => service.CustomJavaPath, customJavaPath);
        settings.SetupProperty(service => service.UseCustomJava, false);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.GameEnvironmentVariables).Returns(string.Empty);
        settings.SetupGet(service => service.InstanceDirectory).Returns(DefaultInstanceDirectory);
        settings.SetupGet(service => service.DefaultInstanceDirectory).Returns(DefaultInstanceDirectory);
        settings.SetupGet(service => service.LauncherDataDirectory).Returns(LauncherDataDirectory);
        return settings;
    }
}
