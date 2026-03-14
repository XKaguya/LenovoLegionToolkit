# Lenovo Legion Toolkit - Installer Signing Script
# This script stamps the compiled installer executable with the project's Authenticode
# certificate. This allows the custom updater to verify the installer's signature.

param(
    [string]$InstallerPath = "build_installer\LenovoLegionToolkitSetup.exe",
    [string]$PfxPath = "LenovoLegionToolkit.pfx",
    [string]$Password = $env:LLT_CERT_PASSWORD
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PfxPath) -or [string]::IsNullOrWhiteSpace($Password)) {
    if ($env:CI -eq "true") {
        throw "Missing certificate or password to sign the installer in CI environment."
    }
    Write-Warning "Skipping installer signing. Certificate or password not found."
    return
}

Write-Host "Stamping installer: $InstallerPath"
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $Password)
Set-AuthenticodeSignature -FilePath $InstallerPath -Certificate $cert -TimestampServer "http://timestamp.digicert.com"

Write-Host "Installer stamped successfully." -ForegroundColor Green
