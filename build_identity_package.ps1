param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter()]
    [string]$OutputDir = "build",

    [Parameter()]
    [string]$PfxPath = "LenovoLegionToolkit.LampArray.pfx",

    [Parameter()]
    [string]$Password = $env:LLT_CERT_PASSWORD
)

$ErrorActionPreference = "Stop"

function Get-SdkToolPath {
    param(
        [Parameter(Mandatory)]
        [string]$ToolName
    )

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $sdkRoot)) {
        return $null
    }

    $candidate = Get-ChildItem $sdkRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$ToolName" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    return $candidate
}

function Write-FallbackIdentityFiles {
    param(
        [Parameter(Mandatory)]
        [string]$DestinationDir,

        [Parameter(Mandatory)]
        [string]$ResolvedVersion,

        [Parameter(Mandatory)]
        [string]$ManifestSource
    )

    Write-Warning "MakeAppx.exe was not found. Falling back to raw manifest registration for this local build."

    $manifestDestination = Join-Path $DestinationDir "AppxManifest.xml"
    $imagesDestination = Join-Path $DestinationDir "Images"
    New-Item -ItemType Directory -Path $imagesDestination -Force | Out-Null
    Copy-Item "LenovoLegionToolkit.LampArray\Images\*" -Destination $imagesDestination -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $DestinationDir "public") -Force | Out-Null

    [xml]$fallbackManifest = Get-Content $ManifestSource
    $fallbackManifest.Package.Identity.Version = $ResolvedVersion
    $fallbackManifest.Save($manifestDestination)

    $priSource = Join-Path "LenovoLegionToolkit.LampArray" "resources.pri"
    if (Test-Path $priSource) {
        Copy-Item $priSource -Destination (Join-Path $DestinationDir "resources.pri") -Force
    }

    if (-not (Test-Path "LenovoLegionToolkit.LampArray.cer")) {
        throw "Public certificate not found: LenovoLegionToolkit.LampArray.cer"
    }

    Copy-Item "LenovoLegionToolkit.LampArray.cer" -Destination (Join-Path $DestinationDir "LenovoLegionToolkit.LampArray.cer") -Force
}

$normalizedVersion = [Version]$Version
$resolvedVersionParts = @(
    $normalizedVersion.Major,
    $normalizedVersion.Minor,
    $(if ($normalizedVersion.Build -ge 0) { $normalizedVersion.Build } else { 0 }),
    $(if ($normalizedVersion.Revision -ge 0) { $normalizedVersion.Revision } else { 0 })
)
$resolvedVersion = $resolvedVersionParts -join "."
$resolvedOutputDir = Join-Path (Resolve-Path ".").Path $OutputDir
$stagingDir = Join-Path $resolvedOutputDir "identity_package"
$msixPath = Join-Path $resolvedOutputDir "LenovoLegionToolkit.LampArray.msix"
$manifestSource = "LenovoLegionToolkit.LampArray\Package.appxmanifest"
$manifestDestination = Join-Path $stagingDir "AppxManifest.xml"

$makeAppx = Get-SdkToolPath -ToolName "MakeAppx.exe"
$signTool = Get-SdkToolPath -ToolName "SignTool.exe"

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}

if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

if (-not $makeAppx -or -not $signTool) {
    if ($env:CI -eq "true") {
        $missingTools = @()
        if (-not $makeAppx) {
            $missingTools += 'MakeAppx.exe'
        }

        if (-not $signTool) {
            $missingTools += 'SignTool.exe'
        }

        throw "Windows packaging tools are required in CI. Missing: $($missingTools -join ', ')"
    }

    Write-FallbackIdentityFiles -DestinationDir $resolvedOutputDir -ResolvedVersion $resolvedVersion -ManifestSource $manifestSource
    Write-Host "Prepared fallback identity registration files in: $resolvedOutputDir"
    return
}

if (-not (Test-Path $PfxPath)) {
    throw "Signing certificate not found: $PfxPath"
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    if ($env:CI -eq "true" -or [Console]::IsInputRedirected) {
        throw "Certificate password is required. Pass -Password or set LLT_CERT_PASSWORD."
    }

    $securePassword = Read-Host "Enter certificate password" -AsSecureString
    $passwordPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try {
        $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPtr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPtr)
    }

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "Certificate password is required. Pass -Password or set LLT_CERT_PASSWORD."
    }
}

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
$stagingImagesDestination = Join-Path $stagingDir "Images"
New-Item -ItemType Directory -Path $stagingImagesDestination -Force | Out-Null
Copy-Item "LenovoLegionToolkit.LampArray\Images\*" -Destination $stagingImagesDestination -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $stagingDir "public") -Force | Out-Null

[xml]$manifest = Get-Content $manifestSource
$manifest.Package.Identity.Version = $resolvedVersion
$manifest.Save($manifestDestination)

& $makeAppx pack /o /d $stagingDir /nv /p $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE"
}

& $signTool sign /fd SHA256 /f $PfxPath /p $Password $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path "LenovoLegionToolkit.LampArray.cer")) {
    throw "Public certificate not found: LenovoLegionToolkit.LampArray.cer"
}

Copy-Item "LenovoLegionToolkit.LampArray.cer" -Destination (Join-Path $resolvedOutputDir "LenovoLegionToolkit.LampArray.cer") -Force
Write-Host "Built identity package: $msixPath"