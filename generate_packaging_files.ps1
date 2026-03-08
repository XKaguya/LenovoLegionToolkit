# Lenovo Legion Toolkit - Packaging Helper Script
# This script generates a self-signed certificate and prepares the manifest for Sparse Packaging.
# Run this script once to set up your environment or when you need to regenerate certificates.
param(
    [Parameter()]
    [string]$Password = $env:LLT_CERT_PASSWORD
)

$Publisher = "CN=LenovoLegionToolkit"
$CertPath = "LenovoLegionToolkit.LampArray.pfx"
$PublicPath = "LenovoLegionToolkit.LampArray.cer"

Write-Host "--- Lenovo Legion Toolkit Packaging Prep ---" -ForegroundColor Cyan

# 1. Generate the Certificate if it doesn't exist
if (-not (Test-Path $CertPath)) {
    Write-Host "Generating self-signed certificate for $Publisher..." -ForegroundColor Green
    
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
        -KeyUsage DigitalSignature -FriendlyName "LLT Packaging Certificate" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    
    $pwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $CertPath -Password $pwd
    Export-Certificate -Cert $cert -FilePath $PublicPath

    # 1a. Tidy up - Remove the cert from the store now that we have the files
    Get-Item $cert.PSPath | Remove-Item
    
    Write-Host "Created $CertPath and $PublicPath" -ForegroundColor Green
    Write-Host "Removed temporary certificate from your Personal Store (tidy up)." -ForegroundColor Gray
} else {
    Write-Host "Certificate files already exist. Skipping generation." -ForegroundColor Yellow
}

# 2. Prepare the build folder (optional helper)
$BuildDir = "BuildLLT"
if (Test-Path $BuildDir) {
    Write-Host "Copying manifests to $BuildDir..." -ForegroundColor Green
    Copy-Item "LenovoLegionToolkit.LampArray\Package.appxmanifest" -Destination "$BuildDir\AppxManifest.xml" -Force
    Copy-Item $PublicPath -Destination "$BuildDir\$PublicPath" -Force
    Write-Host "Done. Your build folder is ready for the installer." -ForegroundColor Green
}

Write-Host "Packaging prep complete." -ForegroundColor Cyan
