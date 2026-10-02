param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

try {
    $exe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
    $certificateDirectory = Join-Path (Split-Path -Parent $exe) "Certificate"
    $certificatePath = Join-Path $certificateDirectory "JBZ-CodeSigning.cer"
    $installerPath = Join-Path $certificateDirectory "INSTALL_JBZ_CERTIFICATE.cmd"
    if (-not (Test-Path -LiteralPath $certificatePath) -or
        -not (Test-Path -LiteralPath $installerPath)) {
        throw "The public certificate or installer is missing from the publish output."
    }

    $publicCertificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne "Valid" -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -ne "CN=JBZ" -or
        $signature.SignerCertificate.Thumbprint -ne $publicCertificate.Thumbprint -or
        $publicCertificate.HasPrivateKey) {
        throw "Final Authenticode verification failed: $($signature.Status) ($($signature.StatusMessage))."
    }

    Write-Host "========================================"
    Write-Host " JBZ BUILD COMPLETED"
    Write-Host "========================================"
    Write-Host "EXE: $exe"
    Write-Host "Code Signing: VALID"
    Write-Host "Publisher: $($signature.SignerCertificate.Subject)"
    Write-Host "Certificate Thumbprint: $($signature.SignerCertificate.Thumbprint)"
    Write-Host "Public Certificate: $certificatePath"
    Write-Host "Client certificate installer: $installerPath"
    Write-Host "========================================"
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
