param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Assert-UnlinkedPath([string]$Root) {
    for ($ancestor = [IO.DirectoryInfo]$Root; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
        if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing linked release path: $($ancestor.FullName)"
        }
    }
}

function Get-Manifest([string]$Root) {
    Assert-UnlinkedPath $Root
    $manifest = @{}
    foreach ($entry in Get-ChildItem -LiteralPath $Root -Force -Recurse) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing linked release entry: $($entry.FullName)"
        }
        if (-not $entry.PSIsContainer) {
            $relative = $entry.FullName.Substring($Root.TrimEnd('\').Length + 1)
            $manifest[$relative] = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
        }
    }
    return $manifest
}

function Assert-Manifest($Expected, [string]$Root) {
    $actual = Get-Manifest $Root
    foreach ($relative in $Expected.Keys) {
        if (-not $actual.ContainsKey($relative) -or $actual[$relative] -ne $Expected[$relative]) {
            throw "Release SHA256 verification failed: $relative in $Root"
        }
    }
}

function Copy-Tree([string]$From, [string]$To) {
    # No /MIR: retain destination runtime files absent from the new package.
    $ErrorActionPreference = 'Continue'
    $output = & (Join-Path $env:SystemRoot 'System32\robocopy.exe') $From $To /E /COPY:DAT /DCOPY:DAT /XJ /R:0 /W:0 /NP 2>&1
    $code = $LASTEXITCODE
    if ($code -ge 8) { throw "Robocopy failed ($code): $($output -join [Environment]::NewLine)" }
}

try {
    $Source = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    $Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if ($Source.Equals($Destination, [StringComparison]::OrdinalIgnoreCase) -or
        $Source.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $Destination.StartsWith($Source + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release source and destination must be separate directories.'
    }
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { throw "Missing release source: $Source" }
    if (-not (Test-Path -LiteralPath (Join-Path $Source 'JBZUniversalTester.exe') -PathType Leaf)) {
        throw 'The release package has no JBZUniversalTester.exe.'
    }
    $expected = Get-Manifest $Source
    if (Test-Path -LiteralPath $Destination) {
        # Preserve the entire previous folder, including CFG, SQLite/WAL and
        # counters, before any release file can overwrite an existing name.
        $previous = Get-Manifest $Destination
        $backup = Join-Path (Split-Path -Parent $Destination) ('.preserved\' +
            (Split-Path -Leaf $Destination) + '.' + (Get-Date -Format 'yyyyMMdd_HHmmss_fffffff') + '.' + [guid]::NewGuid().ToString('N'))
        Assert-UnlinkedPath $backup
        Copy-Tree $Destination $backup
        Assert-Manifest $previous $backup
        Assert-Manifest $previous $Destination
        Write-Host "[LAN] Previous folder preserved and verified: $backup"
    } else {
        # Validate existing parents before creating/writing the destination.
        $parent = Split-Path -Parent $Destination
        Assert-UnlinkedPath $parent
    }
    Copy-Tree $Source $Destination
    Assert-Manifest $expected $Destination
    Write-Host "[LAN] SHA256 verified: $($expected.Count) files; destination=$Destination"
    exit 0
} catch {
    [Console]::Error.WriteLine("[LAN] Copy/verification failed: " + $_.Exception.Message)
    exit 1
}
