// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;

namespace Hyprism.Core.Models;

/// <summary>Paged search response from the CurseForge Search API</summary>
public class CurseForgeSearchResponse
{
    /// <summary>Mods returned for the current search page</summary>
    public List<CurseForgeMod>? Data { get; set; }
    /// <summary>Pagination metadata for the search response</summary>
    public CurseForgePagination? Pagination { get; set; }
}

/// <summary>Single-mod response from the CurseForge Mod API</summary>
public class CurseForgeModResponse
{
    /// <summary>Mod returned by the API</summary>
    public CurseForgeMod? Data { get; set; }
}

/// <summary>Pagination metadata included in CurseForge list responses</summary>
public class CurseForgePagination
{
    /// <summary>Zero-based page index</summary>
    public int Index { get; set; }
    /// <summary>Maximum number of items requested for the page</summary>
    public int PageSize { get; set; }
    /// <summary>Number of items returned in the page</summary>
    public int ResultCount { get; set; }
    /// <summary>Total number of matching items</summary>
    public int TotalCount { get; set; }
}

/// <summary>Represents a single mod entry from the CurseForge API</summary>
public class CurseForgeMod
{
    /// <summary>CurseForge mod identifier</summary>
    public int Id { get; set; }
    /// <summary>Mod display name</summary>
    public string? Name { get; set; }
    /// <summary>Mod URL slug</summary>
    public string? Slug { get; set; }
    /// <summary>Short mod description</summary>
    public string? Summary { get; set; }
    /// <summary>Total number of downloads</summary>
    public int DownloadCount { get; set; }
    /// <summary>ISO 8601 creation timestamp</summary>
    public string? DateCreated { get; set; }
    /// <summary>ISO 8601 last-modified timestamp</summary>
    public string? DateModified { get; set; }
    /// <summary>Mod logo metadata</summary>
    public CurseForgeLogo? Logo { get; set; }
    /// <summary>Categories assigned to the mod</summary>
    public List<CurseForgeCategory>? Categories { get; set; }
    /// <summary>Authors credited for the mod</summary>
    public List<CurseForgeAuthor>? Authors { get; set; }
    /// <summary>Latest files returned with the mod</summary>
    public List<CurseForgeFile>? LatestFiles { get; set; }
    /// <summary>Screenshots attached to the mod</summary>
    public List<CurseForgeScreenshot>? Screenshots { get; set; }
}

/// <summary>A screenshot attached to a CurseForge mod</summary>
public class CurseForgeScreenshot
{
    /// <summary>Screenshot identifier</summary>
    public int Id { get; set; }
    /// <summary>Screenshot title</summary>
    public string? Title { get; set; }
    /// <summary>Thumbnail image URL</summary>
    public string? ThumbnailUrl { get; set; }
    /// <summary>Full-size image URL</summary>
    public string? Url { get; set; }
}

/// <summary>Logo/thumbnail image for a CurseForge mod</summary>
public class CurseForgeLogo
{
    /// <summary>Logo identifier</summary>
    public int Id { get; set; }
    /// <summary>Thumbnail image URL</summary>
    public string? ThumbnailUrl { get; set; }
    /// <summary>Full-size image URL</summary>
    public string? Url { get; set; }
}

/// <summary>A mod category from CurseForge</summary>
public class CurseForgeCategory
{
    /// <summary>Category identifier</summary>
    public int Id { get; set; }
    /// <summary>Category display name</summary>
    public string? Name { get; set; }
    /// <summary>Category URL slug</summary>
    public string? Slug { get; set; }
    /// <summary>Parent category identifier</summary>
    public int ParentCategoryId { get; set; }
    /// <summary>Whether the category is a class category</summary>
    public bool? IsClass { get; set; }
}

/// <summary>Author entry for a CurseForge mod</summary>
public class CurseForgeAuthor
{
    /// <summary>Author identifier</summary>
    public int Id { get; set; }
    /// <summary>Author display name</summary>
    public string? Name { get; set; }
    /// <summary>Author profile URL</summary>
    public string? Url { get; set; }
    /// <summary>Author avatar URL</summary>
    public string? AvatarUrl { get; set; }
}

/// <summary>A specific file/release attached to a CurseForge mod</summary>
public class CurseForgeFile
{
    /// <summary>File identifier</summary>
    public int Id { get; set; }
    /// <summary>Parent mod identifier</summary>
    public int ModId { get; set; }
    /// <summary>Human-readable file name</summary>
    public string? DisplayName { get; set; }
    /// <summary>File name used by the download</summary>
    public string? FileName { get; set; }
    /// <summary>Direct download URL</summary>
    public string? DownloadUrl { get; set; }
    /// <summary>File size in bytes</summary>
    public long FileLength { get; set; }
    /// <summary>ISO 8601 release timestamp</summary>
    public string? FileDate { get; set; }
    /// <summary>CurseForge release type identifier</summary>
    public int ReleaseType { get; set; }
    /// <summary>Total number of downloads for the file</summary>
    public int DownloadCount { get; set; }
    /// <summary>Game versions declared as compatible with the file</summary>
    public List<string>? GameVersions { get; set; }
    /// <summary>Other CurseForge projects related to this file</summary>
    public List<CurseForgeFileDependency>? Dependencies { get; set; }
}

/// <summary>A dependency or compatibility relation declared by a CurseForge file</summary>
public class CurseForgeFileDependency
{
    /// <summary>Related CurseForge project identifier</summary>
    public int ModId { get; set; }
    /// <summary>Related CurseForge file identifier, when the relation targets one file</summary>
    public int FileId { get; set; }
    /// <summary>CurseForge relation type</summary>
    public CurseForgeDependencyRelationType RelationType { get; set; }
}

/// <summary>Categories list response from the CurseForge API</summary>
public class CurseForgeCategoriesResponse
{
    /// <summary>Categories returned by the API</summary>
    public List<CurseForgeCategory>? Data { get; set; }
}

/// <summary>Paged files list response from the CurseForge Files API</summary>
public class CurseForgeFilesResponse
{
    /// <summary>Files returned for the current page</summary>
    public List<CurseForgeFile>? Data { get; set; }
    /// <summary>Pagination metadata for the files response</summary>
    public CurseForgePagination? Pagination { get; set; }
}

/// <summary>Single-file response from the CurseForge Files API</summary>
public class CurseForgeFileResponse
{
    /// <summary>File returned by the API</summary>
    public CurseForgeFile? Data { get; set; }
}

/// <summary>Download URL response from the CurseForge Files API</summary>
public class CurseForgeDownloadUrlResponse
{
    /// <summary>Resolved direct download URL</summary>
    public string? Data { get; set; }
}
