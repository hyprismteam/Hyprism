# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = 'Stop'
$packagingDirectory = Split-Path -Parent $PSCommandPath
$projectRoot = Split-Path -Parent $packagingDirectory
$projectFile = Join-Path $projectRoot 'Sources/Hyprism.Desktop/Hyprism.Desktop.csproj'
$iconFile = Join-Path $projectRoot 'Sources/Hyprism.Desktop/Assets/Images/Hyprism.ico'
$wixSource = Join-Path $packagingDirectory 'windows'
$wixVersion = '6.0.2'
$targets = [System.Collections.Generic.List[string]]::new()
$outputDirectory = Join-Path $projectRoot 'dist'

function Show-Usage {
    @'
Usage: pwsh Packaging/publish-windows.ps1 <target> [<target>...] [options]

Targets:
  all   Build portable ZIP, MSI, and EXE installer
  zip   Build a portable ZIP
  msi   Build an MSI installer
  exe   Build an EXE bootstrapper installer

Options:
  --output <directory>  Artifact directory, defaults to dist
'@ | Write-Output
}

for ($index = 0; $index -lt $Arguments.Count; $index++) {
    switch ($Arguments[$index]) {
        '--output' {
            if (++$index -ge $Arguments.Count) { throw '--output requires a directory' }
            $outputDirectory = $Arguments[$index]
        }
        '--help' { Show-Usage; exit 0 }
        '-h' { Show-Usage; exit 0 }
        'all' { $targets.Add('all') }
        'zip' { $targets.Add('zip') }
        'msi' { $targets.Add('msi') }
        'exe' { $targets.Add('exe') }
        default { throw "Unknown Windows publish target: $($Arguments[$index])" }
    }
}

if ($targets.Count -eq 0 -or $targets.Contains('all')) {
    $targets = [System.Collections.Generic.List[string]]@('zip', 'msi', 'exe')
}

if (-not (Test-Path -Path $iconFile -PathType Leaf)) {
    throw "Windows icon was not found: $iconFile"
}

$version = (& dotnet msbuild $projectFile -nologo -getProperty:Version | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Hyprism.Desktop.csproj does not define a Version property'
}

$artifactVersion = $version -replace '[^0-9A-Za-z._+-]', '-'
$installerVersion = $version.TrimStart('v').Split('-')[0]
if ($installerVersion -notmatch '^\d+(\.\d+){0,2}$') {
    $installerVersion = '0.0.0'
}
$installerVersion = ($installerVersion.Split('.') + '0', '0', '0')[0..2] -join '.'

if (-not [System.IO.Path]::IsPathRooted($outputDirectory)) {
    $outputDirectory = Join-Path $projectRoot $outputDirectory
}
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$buildRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("hyprism-publish-" + [Guid]::NewGuid())
$publishDirectory = Join-Path $buildRoot 'publish'
$wixToolDirectory = Join-Path $buildRoot 'wix'

try {
    New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
    dotnet publish $projectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishReadyToRun=true `
        --output $publishDirectory

    $launcherAppHost = Join-Path $publishDirectory 'Hyprism Launcher.exe'
    if (-not (Test-Path $launcherAppHost)) {
        throw 'Expected Windows apphost was not published: Hyprism Launcher.exe'
    }
    if (-not (Test-Path (Join-Path $publishDirectory 'Hyprism.LocalNode.exe'))) {
        throw 'Expected Windows apphost was not published: Hyprism.LocalNode.exe'
    }

    if ($targets.Contains('zip')) {
        Compress-Archive `
            -Path (Join-Path $publishDirectory '*') `
            -DestinationPath (Join-Path $outputDirectory "Hyprism-win-x64-$artifactVersion.zip") `
            -Force
    }

    if ($targets.Contains('msi') -or $targets.Contains('exe')) {
        dotnet tool install --tool-path $wixToolDirectory wix --version $wixVersion
        $wix = Join-Path $wixToolDirectory 'wix.exe'
        & $wix extension add -g "WixToolset.BootstrapperApplications.wixext/$wixVersion"
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the WiX bootstrapper extension' }

        $msiPath = if ($targets.Contains('msi')) {
            Join-Path $outputDirectory "Hyprism-win-x64-$artifactVersion.msi"
        } else {
            Join-Path $buildRoot 'Hyprism.msi'
        }
        & $wix build (Join-Path $wixSource 'Hyprism.msi.wxs') `
            -arch x64 `
            -d "PublishDir=$publishDirectory" `
            -d "IconPath=$iconFile" `
            -d "ProductVersion=$installerVersion" `
            -o $msiPath
        if ($LASTEXITCODE -ne 0) { throw 'WiX could not build the MSI package' }

        if ($targets.Contains('exe')) {
            & $wix build (Join-Path $wixSource 'Hyprism.bundle.wxs') `
                -arch x64 `
                -ext WixToolset.BootstrapperApplications.wixext `
                -d "IconPath=$iconFile" `
                -d "MsiPath=$msiPath" `
                -d "ProductVersion=$installerVersion" `
                -o (Join-Path $outputDirectory "Hyprism-win-x64-$artifactVersion-setup.exe")
            if ($LASTEXITCODE -ne 0) { throw 'WiX could not build the EXE installer' }
        }
    }
} finally {
    if (Test-Path $buildRoot) {
        Remove-Item -Recurse -Force $buildRoot
    }
}

Write-Output "Published Windows artifacts to $outputDirectory"
