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
$makePri = Join-Path (Resolve-Path ".").Path "MakePRI\makepri.exe"
if (-not (Test-Path $makePri)) {
    $makePri = Get-SdkToolPath -ToolName "makepri.exe"
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}

if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

Remove-Item (Join-Path $resolvedOutputDir "resources*.pri") -ErrorAction SilentlyContinue

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
    
    # Try to generate PRI even in fallback mode if makepri is available
    if ($makePri -and (Test-Path $makePri)) {
        Write-Host "Generating resources.pri for fallback..."
        # Create a temp directory for PRI indexing to avoid indexing the whole build folder
        $priStaging = Join-Path $resolvedOutputDir "pri_staging"
        New-Item -ItemType Directory -Path $priStaging -Force | Out-Null
        Copy-Item (Join-Path $resolvedOutputDir "AppxManifest.xml") -Destination $priStaging -Force
        Copy-Item (Join-Path $resolvedOutputDir "Images") -Destination $priStaging -Recurse -Force
        
        # Ensure neutral resources exist for PRI indexing
        Get-ChildItem -Path $priStaging -Filter "*.scale-200.png" -Recurse | ForEach-Object {
            $neutralPath = $_.FullName -replace '\.scale-200\.png$', '.png'
            if (-not (Test-Path $neutralPath)) {
                Copy-Item $_.FullName -Destination $neutralPath -Force
            }
        }
        
        $configPath = Join-Path $priStaging "priconfig.xml"
        # Generate config without auto-split logic for a single PRI
        & $makePri createconfig /cf $configPath /dq en-US /pv 10.0.0 /o | Out-Null
        & $makePri new /pr $priStaging /cf $configPath /of (Join-Path $resolvedOutputDir "resources.pri") /o /v | Out-Null
        
        Remove-Item $priStaging -Recurse -Force
    }

    Write-Host "Prepared fallback identity registration files in: $resolvedOutputDir"
    return
}

# MakeAppx and SignTool are available — signing is required from here on
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

# Ensure neutral resources exist for PRI indexing
Get-ChildItem -Path $stagingImagesDestination -Filter "*.scale-200.png" -Recurse | ForEach-Object {
    $neutralPath = $_.FullName -replace '\.scale-200\.png$', '.png'
    if (-not (Test-Path $neutralPath)) {
        Copy-Item $_.FullName -Destination $neutralPath -Force
    }
}
New-Item -ItemType Directory -Path (Join-Path $stagingDir "public") -Force | Out-Null

[xml]$manifest = Get-Content $manifestSource
$manifest.Package.Identity.Version = $resolvedVersion
$manifest.Save($manifestDestination)

# Generate PRI before packing
if ($makePri -and (Test-Path $makePri)) {
    Write-Host "Generating resources.pri..."
    $configPath = Join-Path $stagingDir "priconfig.xml"
    & $makePri createconfig /cf $configPath /dq en-US /pv 10.0.0 /o | Out-Null
    & $makePri new /pr $stagingDir /cf $configPath /of (Join-Path $stagingDir "resources.pri") /o /v | Out-Null
    Remove-Item $configPath -ErrorAction SilentlyContinue
}

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