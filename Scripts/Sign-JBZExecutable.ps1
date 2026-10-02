param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$exe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
$certificateDirectory = Join-Path $PublishDirectory "Certificate"
$certificatePath = Join-Path $certificateDirectory "JBZ-CodeSigning.cer"
$installerCmd = Join-Path $PSScriptRoot "INSTALL_JBZ_CERTIFICATE.cmd"
$installerPs1 = Join-Path $PSScriptRoot "Install-JBZCertificate.ps1"
$codeSigningOid = "1.3.6.1.5.5.7.3.3"

if (-not (Test-Path -LiteralPath $installerCmd) -or
    -not (Test-Path -LiteralPath $installerPs1)) {
    throw "The JBZ certificate installer scripts are missing."
}

$matching = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.Subject -eq "CN=JBZ" -and
    @($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq $codeSigningOid }).Count -gt 0
})

if ($matching.Count -gt 1) {
    throw "Multiple CN=JBZ code signing certificates exist in CurrentUser\My. Select one certificate manually before building; no new certificate was created."
}

if ($matching.Count -eq 1) {
    $certificate = $matching[0]
    Write-Host "Reusing existing JBZ Code Signing certificate: $($certificate.Thumbprint)"
}
else {
    $sameSubject = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq "CN=JBZ")
    if ($sameSubject.Count -gt 0) {
        throw "CN=JBZ exists in CurrentUser\My without the Code Signing EKU. Resolve this certificate conflict before building."
    }

    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=JBZ" `
        -FriendlyName "JBZ Code Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(10)
    Write-Host "Created JBZ Code Signing certificate: $($certificate.Thumbprint)"
}

if (-not $certificate.HasPrivateKey -or
    $certificate.NotBefore -gt (Get-Date) -or
    $certificate.NotAfter -le (Get-Date)) {
    throw "The JBZ signing certificate is missing its private key or is outside its validity period."
}

$rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
try {
    if ($null -eq $rsa -or $rsa.KeySize -lt 3072) {
        throw "The JBZ signing certificate must use RSA 3072 bits or stronger."
    }
}
finally {
    if ($null -ne $rsa) { $rsa.Dispose() }
}

if ($certificate.SignatureAlgorithm.FriendlyName -notmatch "sha256") {
    throw "The JBZ signing certificate must use SHA256."
}

New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null
Copy-Item -LiteralPath $installerCmd -Destination $certificateDirectory -Force
Copy-Item -LiteralPath $installerPs1 -Destination $certificateDirectory -Force

$publicCertificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
if ($publicCertificate.HasPrivateKey -or
    $publicCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw "The exported public certificate is invalid or contains a private key."
}

# A self-signed publisher must be trusted on the build workstation for /pa and
# Get-AuthenticodeSignature to report Valid. Only the public certificate is added.
foreach ($storeName in @("Root", "TrustedPublisher")) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        $storeName,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        if (@($store.Certificates | Where-Object Thumbprint -eq $certificate.Thumbprint).Count -eq 0) {
            $store.Add($publicCertificate)
        }
    }
    finally {
        $store.Close()
    }
}

$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $signTool) {
    $kitRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    $hostArchitecture = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
    if (@($kitRoots).Count -gt 0) {
        $signTool = Get-ChildItem -Path $kitRoots -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq $hostArchitecture } |
            Sort-Object -Property @{ Expression = {
                $parsed = [Version]::new(0, 0)
                [Version]::TryParse($_.Directory.Parent.Name, [ref]$parsed) | Out-Null
                $parsed
            }; Descending = $true } |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if (-not $signTool -or -not (Test-Path -LiteralPath $signTool)) {
    throw "signtool.exe was not found in PATH or the installed Windows SDK. Signing cannot continue."
}

# No publish, copy, resource edit, or executable write may run after this call.
& $signTool sign /sha1 $certificate.Thumbprint /s My /fd SHA256 /v $exe | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "signtool sign failed with exit code $LASTEXITCODE."
}

& $signTool verify /pa /v $exe | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "signtool verify /pa failed with exit code $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -LiteralPath $exe
if ($signature.Status -ne "Valid" -or
    $null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw "Authenticode verification failed: $($signature.Status) ($($signature.StatusMessage))."
}

[pscustomobject]@{
    ExePath = $exe
    Status = [string]$signature.Status
    Publisher = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    CertificatePath = $certificatePath
    InstallerPath = Join-Path $certificateDirectory "INSTALL_JBZ_CERTIFICATE.cmd"
}
