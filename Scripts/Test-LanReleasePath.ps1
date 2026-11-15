param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [ValidateSet('Container', 'Leaf')]
    [string]$PathType = 'Container',
    [ValidateRange(1, 60)]
    [int]$TimeoutSeconds = 5
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# A UNC access can block inside Windows longer than the requested timeout.
# Isolate it in a process so even a stalled SMB operation can be terminated.
$literalPath = $Path.Replace("'", "''")
$probe = "try { if (Test-Path -LiteralPath '$literalPath' -PathType $PathType -ErrorAction Stop) { exit 0 }; exit 1 } catch { [Console]::Error.WriteLine(`$_.Exception.Message); exit 1 }"
$encodedProbe = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($probe))
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = Join-Path $PSHOME 'powershell.exe'
$startInfo.Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand $encodedProbe"
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
$result = 1
try {
    [void]$process.Start()
    if ($process.WaitForExit($TimeoutSeconds * 1000)) {
        $result = $process.ExitCode
        if ($result -ne 0) {
            Write-Warning "LAN path is unavailable: $Path"
        }
    } else {
        $process.Kill()
        Write-Warning "LAN path check timed out after $TimeoutSeconds seconds: $Path"
        $result = 2
    }
} catch {
    Write-Warning "LAN path check failed: $($_.Exception.Message)"
} finally {
    $process.Dispose()
}
exit $result
