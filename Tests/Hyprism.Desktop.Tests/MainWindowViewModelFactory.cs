// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Hyprism.Core.Accounts;
using Hyprism.Core.Application.Ports;
using Hyprism.Core.Application.Progress;
using Hyprism.Core.Game;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Game.Launch;
using Hyprism.Core.Game.Sources;
using Hyprism.Core.Game.Versions;
using Hyprism.Core.Models;
using Hyprism.Desktop.Integrations.GitHub;
using Hyprism.Desktop.Screens.News;
using Hyprism.Desktop.Screens.Settings;
using Hyprism.Desktop.Localization;
using Hyprism.Desktop.Platform;
using Hyprism.Desktop.Shell;
using Moq;

namespace Hyprism.Desktop.Tests;

/// <summary>
/// Builds a fully mocked <see cref="MainWindowViewModel"/> with realistic sample data
/// for documentation screenshots
/// </summary>
internal static class MainWindowViewModelFactory
{
    public static MainWindowViewModel Create(
        HttpClient httpClient,
        IMirrorCatalog? mirrorCatalog = null,
        IGameVersionCatalog? versionCatalog = null,
        string language = "en-US")
    {
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var gitHub = new Mock<IGitHubClient>();
        var gpuProvider = new Mock<IGpuProvider>();

        var installedInstance = new InstanceInfo
        {
            Id = "instance-aurora",
            Name = "Aurora survival",
            Branch = "release",
            Version = 7,
            VersionName = "0.6.4",
            IsInstalled = true
        };
        var snapshotInstance = new InstanceInfo
        {
            Id = "instance-snapshot",
            Name = "Snapshot experiments",
            Branch = "pre-release",
            Version = 6,
            VersionName = "build-6",
            IsInstalled = false
        };

        instances.Setup(service => service.GetCachedInstances())
            .Returns([installedInstance, snapshotInstance]);
        instances.Setup(service => service.GetSelectedInstance())
            .Returns(installedInstance);
        instances.Setup(service => service.GetInstancePathById(It.IsAny<string>()))
            .Returns("/tmp/hyprism-docs-instance");
        instances.Setup(service => service.IsClientPresent(It.IsAny<string>()))
            .Returns(true);

        profile.Setup(service => service.GetNick()).Returns("Aurora");
        profileManagement.Setup(service => service.GetProfiles())
            .Returns(
            [
                new Profile
                {
                    Id = "profile-aurora",
                    Name = "Aurora",
                    UUID = "e7a1c5f05d3f4b1e9d2a8c6b0f3e7a1c",
                    IsOfficial = false
                },
                new Profile
                {
                    Id = "profile-hytale",
                    Name = "Hytale Account",
                    UUID = "9f3b7d2e6c8a4f0b1e5d7c9a3f1b6e8d",
                    IsOfficial = true
                }
            ]);
        profileManagement.Setup(service => service.GetSelectedProfileId())
            .Returns("profile-aurora");
        profileManagement.Setup(service => service.GetSelectedProfile())
            .Returns(new Profile
            {
                Id = "profile-aurora",
                Name = "Aurora",
                UUID = "e7a1c5f05d3f4b1e9d2a8c6b0f3e7a1c",
                IsOfficial = false
            });

        settings.SetupGet(service => service.Language).Returns(language);
        settings.SetupGet(service => service.GpuPreference).Returns("auto");

        gitHub.Setup(service => service.GetContributorsAsync()).ReturnsAsync(
        [
            new GitHubUser { Login = "yyyumeniku", Type = "User" },
            new GitHubUser { Login = "sanasol", Type = "User" },
            new GitHubUser { Login = "DanielFreak", Type = "User" },
            new GitHubUser { Login = "XargonWan", Type = "User" }
        ]);
        gitHub.Setup(service => service.GetLatestMainCommitAsync()).ReturnsAsync(
            new GitHubCommit(
                "1a2b3c4d5e6f7890abcdef1234567890abcdef12",
                "feat: refine the native settings experience",
                "https://github.com/hyprismteam/Hyprism/commit/1a2b3c4"));
        gitHub
            .Setup(service => service.LoadAvatarAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(TinyPngHandler.ImageBytes);

        var russian = language == "ru-RU";
        var sampleTitle = russian ? "Как читать новости" : "Reading news";
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new NewsItemResponse
                {
                    Title = sampleTitle,
                    Excerpt = russian
                        ? "Демонстрационная статья для документации Hyprism"
                        : "A demonstration article for the Hyprism documentation",
                    Url = "https://example.org/news/reading",
                    Date = "2026-01-01",
                    Author = "Hyprism Docs"
                },
                new NewsItemResponse
                {
                    Title = russian ? "Пример второй публикации" : "Another sample post",
                    Excerpt = russian
                        ? "Выберите запись, чтобы открыть ее в лаунчере"
                        : "Select a post to open it inside the launcher",
                    Url = "https://example.org/news/sample",
                    Date = "2025-12-31",
                    Author = "Hyprism Docs"
                }
            ]);
        news.Setup(service => service.GetNewsArticleAsync(It.IsAny<string>()))
            .ReturnsAsync(new NewsArticleResponse
            {
                Title = sampleTitle,
                Url = "https://example.org/news/reading",
                PublishedAt = "2026-01-01",
                Author = "Hyprism Docs",
                Content =
                [
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "text",
                                Text = russian
                                    ? "Это учебный материал для скриншота, а не новость Hytale. " +
                                      "В обычной работе здесь отображается выбранная статья из официальной ленты"
                                    : "This is screenshot sample content, not a Hytale announcement. " +
                                      "In normal use, the selected article from the official feed appears here"
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "heading",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "text",
                                Text = russian ? "Открытие оригинала" : "Opening the original"
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "text",
                                Text = russian
                                    ? "Кнопка «Ссылка» в заголовке открывает исходную страницу в браузере"
                                    : "The Link button in the header opens the original page in your browser"
                            }
                        ]
                    }
                ]
            });

        gpuProvider.Setup(service => service.GetAdapters())
            .Returns(
            [
                new GpuAdapterInfo
                {
                    Name = "NVIDIA GeForce RTX 4070",
                    Vendor = "NVIDIA",
                    Type = "dedicated",
                    PciId = "0000:01:00.0"
                },
                new GpuAdapterInfo
                {
                    Name = "AMD Radeon(TM) Graphics",
                    Vendor = "AMD",
                    Type = "integrated",
                    PciId = "0000:06:00.0"
                }
            ]);

        var versions = new Mock<IGameVersionCatalog>();
        versions
            .Setup(service => service.ProbeSourceAvailabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorSpeedTestResult
            {
                MirrorId = "probe",
                IsAvailable = true,
                HasVersionsForCurrentPlatform = true,
                PingMs = 34
            });

        return new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            httpClient,
            new StringLocalizer(language),
            filePicker: null,
            gitHubClient: gitHub.Object,
            mirrorCatalog: mirrorCatalog,
            versionCatalog: versionCatalog,
            gpuProvider: gpuProvider.Object);
    }

    private sealed class TinyPngHandler : HttpMessageHandler
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public static byte[] ImageBytes => Png;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Png)
            });
    }
}
