Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run INSTALL_JBZ_CERTIFICATE.cmd as Administrator."
    }

    $certificatePath = Join-Path $PSScriptRoot "JBZ-CodeSigning.cer"
    if (-not (Test-Path -LiteralPath $certificatePath)) {
        throw "Public certificate not found: $certificatePath"
    }

    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
    $codeSigningOid = "1.3.6.1.5.5.7.3.3"
    $eku = @($certificate.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] })
    $hasCodeSigningEku = @($eku | ForEach-Object { $_.EnhancedKeyUsages } |
        Where-Object { $_.Value -eq $codeSigningOid }).Count -gt 0
    if ($certificate.Subject -ne "CN=JBZ" -or
        $certificate.HasPrivateKey -or
        -not $hasCodeSigningEku -or
        $certificate.NotAfter -le (Get-Date) -or
        $certificate.NotBefore -gt (Get-Date)) {
        throw "The .cer file is not a valid public JBZ Code Signing certificate."
    }

    Write-Host "Subject    : $($certificate.Subject)"
    Write-Host "Thumbprint : $($certificate.Thumbprint)"
    Write-Host "Expires    : $($certificate.NotAfter)"

    foreach ($storeName in @("Root", "TrustedPublisher")) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            $storeName,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            if (@($store.Certificates | Where-Object Thumbprint -eq $certificate.Thumbprint).Count -eq 0) {
                $store.Add($certificate)
                Write-Host "Installed in LocalMachine\$storeName"
            }
            else {
                Write-Host "Already present in LocalMachine\$storeName"
            }
        }
        finally {
            $store.Close()
        }
    }

    foreach ($storeName in @("Root", "TrustedPublisher")) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            $storeName,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
            if (@($store.Certificates | Where-Object Thumbprint -eq $certificate.Thumbprint).Count -eq 0) {
                throw "Certificate verification failed in LocalMachine\$storeName."
            }
        }
        finally {
            $store.Close()
        }
    }

    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    try {
        $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        if (-not $chain.Build($certificate)) {
            throw "The installed JBZ certificate does not build a trusted certificate chain."
        }
    }
    finally {
        $chain.Dispose()
    }

    Write-Host "JBZ Code Signing certificate installed successfully."
    exit 0
}
catch {
    Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
