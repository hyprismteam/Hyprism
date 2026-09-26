# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

$ErrorActionPreference = 'Stop'

$githubReleaseBaseUrl = 'https://github.com/hyprismteam/Hyprism/releases/latest/download'
$githubIconUrl = 'https://raw.githubusercontent.com/hyprismteam/Hyprism/main/Sources/Hyprism.Desktop/Assets/Images/Hyprism.ico'
$installerHeaders = @{ 'User-Agent' = 'Hyprism-Installer/1.0' }
$appExecutable = 'Hyprism Launcher.exe'
$localNodeExecutable = 'Hyprism.LocalNode.exe'

if ($env:OS -ne 'Windows_NT') {
    throw 'This installer supports Windows only'
}

$architecture = if ($env:PROCESSOR_ARCHITEW6432) {
    $env:PROCESSOR_ARCHITEW6432
} else {
    $env:PROCESSOR_ARCHITECTURE
}
if ($architecture -ne 'AMD64') {
    throw "This release supports Windows x64 only, detected: $architecture"
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw 'LOCALAPPDATA is not set'
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Hyprism'
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$applicationDataDirectory = [Environment]::GetFolderPath('ApplicationData')
$startMenuDirectory = Join-Path $applicationDataDirectory 'Microsoft\Windows\Start Menu\Programs'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('Hyprism-install-' + [Guid]::NewGuid().ToString('N'))

function Invoke-Download {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Invoke-WebRequest `
        -Uri $Uri `
        -OutFile $Destination `
        -Headers $installerHeaders `
        -UseBasicParsing
}

function New-LauncherShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [string]$IconPath
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$IconPath,0"
    $shortcut.Save()
}

New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

try {
    Write-Host '[*] Fetching release metadata'
    $versionJson = Invoke-RestMethod `
        -Uri "$githubReleaseBaseUrl/version.json" `
        -Headers $installerHeaders `
        -UseBasicParsing
    $version = [string]$versionJson.version
    if ($version -notmatch '^[0-9A-Za-z.+-]+$') {
        throw 'The release metadata contains an invalid version'
    }

    $archiveName = "Hyprism-win-x64-$version.zip"
    $archiveUrl = "$githubReleaseBaseUrl/$archiveName"
    $checksumUrl = "$archiveUrl.sha256"
    $archivePath = Join-Path $temporaryDirectory $archiveName
    $checksumPath = "$archivePath.sha256"
    $payloadDirectory = Join-Path $temporaryDirectory 'payload'
    $iconPath = Join-Path $installDirectory 'Hyprism.ico'
    $targetExePath = Join-Path $installDirectory $appExecutable

    Write-Host "[*] Downloading Hyprism Launcher $version"
    Invoke-Download -Uri $archiveUrl -Destination $archivePath
    Invoke-Download -Uri $checksumUrl -Destination $checksumPath

    $expectedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0]
    if ($expectedHash -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'The release checksum is invalid'
    }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($expectedHash, $actualHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The downloaded archive failed SHA-256 verification'
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $payloadDirectory -Force
    $payloadExecutable = Join-Path $payloadDirectory $appExecutable
    if (-not (Test-Path -LiteralPath $payloadExecutable -PathType Leaf)) {
        throw "The archive does not contain '$appExecutable'"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory $localNodeExecutable) -PathType Leaf)) {
        throw "The archive does not contain '$localNodeExecutable'"
    }

    Invoke-Download -Uri $githubIconUrl -Destination (Join-Path $temporaryDirectory 'Hyprism.ico')
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $payloadDirectory -Force | Copy-Item -Destination $installDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $temporaryDirectory 'Hyprism.ico') -Destination $iconPath -Force

    $shortcutTargets = @()
    if (-not [string]::IsNullOrWhiteSpace($desktopDirectory)) {
        $shortcutTargets += Join-Path $desktopDirectory 'Hyprism Launcher.lnk'
    }
    New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
    $shortcutTargets += Join-Path $startMenuDirectory 'Hyprism Launcher.lnk'
    foreach ($shortcutPath in $shortcutTargets) {
        New-LauncherShortcut `
            -Path $shortcutPath `
            -TargetPath $targetExePath `
            -WorkingDirectory $installDirectory `
            -IconPath $iconPath
        Write-Host "[+] Created shortcut: $shortcutPath"
    }

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $pathEntries = if ([string]::IsNullOrWhiteSpace($userPath)) {
        @()
    } else {
        $userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    $normalizedInstallDirectory = $installDirectory.TrimEnd('\')
    $hasInstallDirectory = $pathEntries | Where-Object {
        [string]::Equals($_.Trim().TrimEnd('\'), $normalizedInstallDirectory, [StringComparison]::OrdinalIgnoreCase)
    }
    if (-not $hasInstallDirectory) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
            $installDirectory
        } else {
            "$userPath;$installDirectory"
        }
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
        $env:Path = "$env:Path;$installDirectory"
        Write-Host '[+] Added the install directory to the user PATH'
    }

    Write-Host "[+] Installed Hyprism Launcher $version"
    Write-Host "[+] Executable: $targetExePath"
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
