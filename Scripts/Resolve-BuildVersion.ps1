param(
    [switch]$Preview
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$versionPath = Join-Path $root "Version.props"

if (-not (Test-Path -LiteralPath $versionPath)) {
    throw "Version.props was not found: $versionPath"
}

function Get-VersionGroup {
    param([xml]$Document)

    $group = @($Document.Project.PropertyGroup)[0]
    if ($null -eq $group) {
        throw "Version.props does not contain a PropertyGroup."
    }

    return $group
}

function Assert-SynchronizedVersion {
    param($Group)

    $release = [string]$Group.Version
    if ([string]::IsNullOrWhiteSpace($release)) {
        throw "Version.props: Version is empty."
    }

    $parsed = [Version]::Parse($release)
    $expectedAssembly = $release + ".0"
    $expectedTag = $release.Replace('.', '_')
    $expectedTitle = "JBZUniversalTester V" + $release

    if ([string]$Group.VersionPrefix -ne $release -or
        [string]$Group.AssemblyVersion -ne $expectedAssembly -or
        [string]$Group.FileVersion -ne $expectedAssembly -or
        [string]$Group.InformationalVersion -ne $release -or
        [string]$Group.VersionFileTag -ne $expectedTag -or
        [string]$Group.AssemblyTitle -ne $expectedTitle) {
        throw "Version.props fields are not synchronized for V$release."
    }

    return $parsed
}

[xml]$workingXml = Get-Content -LiteralPath $versionPath -Raw
$workingGroup = Get-VersionGroup -Document $workingXml
[Version]$workingVersion = Assert-SynchronizedVersion -Group $workingGroup

$headText = @(& git -C $root show "HEAD:Version.props" 2>$null)
if ($LASTEXITCODE -ne 0 -or $headText.Count -eq 0) {
    throw "Cannot read Version.props from Git HEAD."
}

[xml]$headXml = $headText -join [Environment]::NewLine
$headGroup = Get-VersionGroup -Document $headXml
[Version]$headVersion = Assert-SynchronizedVersion -Group $headGroup

if ($workingVersion -lt $headVersion) {
    throw "Working version V$workingVersion is older than Git HEAD V$headVersion."
}

$statusLines = @(& git -C $root status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw "Cannot read Git working-tree status."
}

$sourceChanges = @($statusLines | Where-Object {
    $line = [string]$_
    if ($line.Length -lt 4) {
        return $false
    }

    $path = $line.Substring(3).Trim()
    if ($path.Contains(" -> ")) {
        $path = ($path -split " -> ", 2)[1].Trim()
    }

    $path = $path.Trim('"')
    return -not $path.Equals("Version.props", [StringComparison]::OrdinalIgnoreCase)
})

$release = $workingVersion.ToString()
$action = "UNCHANGED_REBUILD"

if ($workingVersion -eq $headVersion -and $sourceChanges.Count -gt 0) {
    $patch = if ($workingVersion.Build -lt 0) { 0 } else { $workingVersion.Build }
    $next = New-Object Version($workingVersion.Major, $workingVersion.Minor, ($patch + 1))
    $release = $next.ToString()
    $tag = $release.Replace('.', '_')

    $workingGroup.VersionPrefix = $release
    $workingGroup.Version = $release
    $workingGroup.AssemblyVersion = $release + ".0"
    $workingGroup.FileVersion = $release + ".0"
    $workingGroup.InformationalVersion = $release
    $workingGroup.VersionFileTag = $tag
    $workingGroup.AssemblyTitle = "JBZUniversalTester V" + $release

    if ($Preview) {
        $action = "AUTO_INCREMENT_REQUIRED"
    }
    else {
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Indent = $true
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
        $writer = [System.Xml.XmlWriter]::Create($versionPath, $settings)
        try {
            $workingXml.Save($writer)
        }
        finally {
            $writer.Dispose()
        }
        $action = "AUTO_INCREMENTED"
    }
}
elseif ($workingVersion -gt $headVersion) {
    $action = "ALREADY_INCREMENTED"
}

Write-Output ("{0}|{1}" -f $release, $action)
