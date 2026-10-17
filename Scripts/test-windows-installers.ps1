# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [string]$ArtifactDirectory = 'dist',
    [string]$LogDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'hyprism-installer-tests')
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Installer tests require Windows' }

$launcherPath = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Hyprism\Hyprism Launcher.exe'
$shortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Hyprism Launcher.lnk'),
    (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Hyprism Launcher.lnk')
)
foreach ($path in @($launcherPath) + $shortcutPaths) {
    if (Test-Path -LiteralPath $path) { throw "Installer tests require a clean machine: $path already exists" }
}

New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
$installer = New-Object -ComObject WindowsInstaller.Installer

function Get-ComProperty($Object, [string]$Name, [object[]]$PropertyArguments) {
    for ($index = 0; $index -lt $PropertyArguments.Length; $index++) {
        $PropertyArguments[$index] = $PropertyArguments[$index].PSObject.BaseObject
    }
    $Object.GetType().InvokeMember($Name, [System.Reflection.BindingFlags]::GetProperty, $null, $Object, $PropertyArguments)
}

function Invoke-Installer([string]$FilePath, [string]$InstallerArguments, [string]$LogPath) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $InstallerArguments -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        if (Test-Path -LiteralPath $LogPath) { Get-Content -LiteralPath $LogPath | Write-Host }
        throw "Installer exited with code $($process.ExitCode): $FilePath"
    }
}

foreach ($pattern in @('*.msi', '*-setup.exe')) {
    $packages = @(Get-ChildItem -Path (Join-Path $ArtifactDirectory $pattern) -File)
    if ($packages.Count -ne 1) { throw "Expected one package matching $pattern" }
    $package = $packages[0]
    $installLog = Join-Path $LogDirectory "$($package.Name)-install.log"
    $uninstallLog = Join-Path $LogDirectory "$($package.Name)-uninstall.log"
    $isMsi = $package.Extension -eq '.msi'
    $command = if ($isMsi) { 'msiexec.exe' } else { $package.FullName }
    $installArguments = if ($isMsi) {
        "/i `"$($package.FullName)`" /qn /norestart /l*v `"$installLog`""
    } else {
        "/install /quiet /norestart /log `"$installLog`""
    }
    $uninstallArguments = if ($isMsi) {
        "/x `"$($package.FullName)`" /qn /norestart /l*v `"$uninstallLog`""
    } else {
        "/uninstall /quiet /norestart /log `"$uninstallLog`""
    }

    Invoke-Installer $command $installArguments $installLog
    try {
        if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) { throw "Launcher missing: $launcherPath" }
        foreach ($shortcutPath in $shortcutPaths) {
            if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) { throw "Shortcut missing: $shortcutPath" }
            $target = Get-ComProperty $installer 'ShortcutTarget' @($shortcutPath)
            $productCode = Get-ComProperty $target 'StringData' @(1)
            $componentCode = Get-ComProperty $target 'StringData' @(3)
            $targetPath = Get-ComProperty $installer 'ComponentPath' @($productCode, $componentCode)
            if ($targetPath -ne $launcherPath) { throw "Shortcut points to the wrong executable: $targetPath" }
        }
    } finally {
        Invoke-Installer $command $uninstallArguments $uninstallLog
    }

    foreach ($path in @($launcherPath) + $shortcutPaths) {
        if (Test-Path -LiteralPath $path) { throw "Uninstall left a file behind: $path" }
    }
    Write-Output "Install, shortcut targets, and uninstall passed: $($package.Name)"
}
