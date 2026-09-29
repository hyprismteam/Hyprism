// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace Hyprism.Desktop.Screens.News;

/// <summary>A news item returned from the launcher news API or Hytale blog feed</summary>
public class NewsItemResponse
{
    /// <summary>Headline/title of the news article</summary>
    public string Title { get; set; } = "";
    
    /// <summary>Short excerpt or summary text</summary>
    public string Excerpt { get; set; } = "";
    
    /// <summary>Full URL to the article page</summary>
    public string Url { get; set; } = "";
    
    /// <summary>Publication date (ISO 8601 or display string)</summary>
    public string Date { get; set; } = "";

    /// <summary>ISO 8601 publication timestamp</summary>
    public string PublishedAt { get; set; } = "";
    
    /// <summary>Display name of the article author</summary>
    public string Author { get; set; } = "";
    
    /// <summary>URL of the article cover image; may be null</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Category labels assigned by the news publisher</summary>
    public List<string> Categories { get; set; } = [];
    
}

/// <summary>A complete, sanitized news article ready for a native renderer</summary>
public sealed class NewsArticleResponse
{
    /// <summary>Headline/title of the article</summary>
    public string Title { get; set; } = "";

    /// <summary>Short summary shown in the news feed</summary>
    public string Excerpt { get; set; } = "";

    /// <summary>Canonical URL of the article</summary>
    public string Url { get; set; } = "";

    /// <summary>Publication date in ISO 8601 format</summary>
    public string PublishedAt { get; set; } = "";

    /// <summary>Display name of the article author</summary>
    public string Author { get; set; } = "";

    /// <summary>URL of the article cover image; may be null</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Category labels assigned by the publisher</summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>Sanitized article body represented as formatting-aware content nodes</summary>
    public List<NewsContentNode> Content { get; set; } = [];
}

/// <summary>
/// A safe node in a news article content tree. Supported kinds include text, paragraph,
/// heading, image, inline-image, link, bold, italic, blockquote, details, summary,
/// list, list-item, code, YouTube video and divider.
/// </summary>
public sealed class NewsContentNode
{
    /// <summary>Stable renderer-facing node kind</summary>
    public string Kind { get; set; } = "";

    /// <summary>Text payload for text and code nodes</summary>
    public string? Text { get; set; }

    /// <summary>Safe absolute URL for link and media nodes</summary>
    public string? Url { get; set; }

    /// <summary>Safe absolute URL for image nodes</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Accessible alternative text for image nodes</summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Optional renderer hint for inline media, for example "emote" or "sticker".
    /// </summary>
    public string? ImagePresentation { get; set; }

    /// <summary>Heading level from 1 through 6</summary>
    public int? Level { get; set; }

    /// <summary>Nested inline or block content</summary>
    public List<NewsContentNode> Children { get; set; } = [];
}
