// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text;
using Hyprism.Desktop.Integrations.GitHub;
using Xunit;

namespace Hyprism.Desktop.Tests;

public sealed class GitHubClientTests
{
    [Fact]
    public async Task RepositoryMetadataUsesTeamRepositoryAndCachesRequests()
    {
        var handler = new GitHubHandler();
        using var client = new HttpClient(handler);
        var service = new GitHubClient(client);

        var contributors = await service.GetContributorsAsync();
        var contributorsAgain = await service.GetContributorsAsync();
        var commit = await service.GetLatestMainCommitAsync();
        var commitAgain = await service.GetLatestMainCommitAsync();

        Assert.Equal(2, contributors.Count);
        Assert.Equal(2, contributorsAgain.Count);
        Assert.Equal("human", contributors[0].Login);
        Assert.Equal(12, contributors[0].Contributions);
        Assert.NotNull(commit);
        Assert.Same(commit, commitAgain);
        Assert.Equal("abcdef1", commit!.Sha[..7]);
        Assert.Equal("feat: polish About page", commit.Message);
        Assert.Equal(1, handler.Requests.Count(uri => uri.AbsolutePath.EndsWith("/contributors")));
        Assert.Equal(1, handler.Requests.Count(uri => uri.AbsolutePath.EndsWith("/commits")));
        Assert.Equal(1, handler.Requests.Count(uri => uri.AbsolutePath.EndsWith("/commits/main")));
        Assert.All(handler.Requests, uri => Assert.Contains("/repos/hyprismteam/Hyprism/", uri.AbsolutePath));
    }

    [Fact]
    public async Task AvatarDownloadsAreSizedAndCached()
    {
        var handler = new GitHubHandler();
        using var client = new HttpClient(handler);
        var service = new GitHubClient(client);

        var first = await service.LoadAvatarAsync("https://avatars.githubusercontent.com/u/1", 96);
        var second = await service.LoadAvatarAsync("https://avatars.githubusercontent.com/u/1", 96);

        Assert.Equal([1, 2, 3], first);
        Assert.Equal([1, 2, 3], second);
        var avatarRequest = Assert.Single(
            handler.Requests,
            uri => uri.Host == "avatars.githubusercontent.com");
        Assert.Equal("?s=96", avatarRequest.Query);
    }

    private sealed class GitHubHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);

            if (uri.Host == "avatars.githubusercontent.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                });
            }

            var json = uri.AbsolutePath.EndsWith("/contributors", StringComparison.Ordinal)
                ? """
                  [
                    {
                      "login": "stale",
                      "avatar_url": "https://avatars.githubusercontent.com/u/2",
                      "html_url": "https://github.com/stale",
                      "type": "User",
                      "contributions": 99
                    },
                    {
                      "login": "human",
                      "avatar_url": "https://avatars.githubusercontent.com/u/1",
                      "html_url": "https://github.com/human",
                      "type": "User",
                      "contributions": 12
                    }
                  ]
                  """
                : uri.AbsolutePath.EndsWith("/commits", StringComparison.Ordinal)
                    ? """
                      [
                        {
                          "author": {
                            "login": "human"
                          }
                        }
                      ]
                      """
                : """
                  {
                    "sha": "abcdef1234567890",
                    "html_url": "https://github.com/hyprismteam/Hyprism/commit/abcdef1",
                    "commit": {
                      "message": "feat: polish About page\n\nAdditional details"
                    }
                  }
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
