# Lenovo Legion Toolkit - Identity Packaging Script
# This script handles the generation of the resources.pri file and the creation of
# the signed MSIX package (CI) or raw manifest files (Local) for Package Identity.

param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter()]
    [string]$OutputDir = "build",

    [Parameter()]
    [string]$PfxPath = "LenovoLegionToolkit.pfx",

    [Parameter()]
    [string]$Password = $env:LLT_CERT_PASSWORD,

    [Parameter()]
    [switch]$UseManifest
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
    if (Test-Path $sdkRoot) {
        $candidate = Get-ChildItem $sdkRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\$ToolName" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1

        if ($candidate) {
            return $candidate
        }
    }

    $nugetPackages = Join-Path $env:UserProfile ".nuget\packages\microsoft.windows.sdk.buildtools"
    if (Test-Path $nugetPackages) {
        $candidate = Get-ChildItem $nugetPackages -Directory |
            Sort-Object Name -Descending |
            ForEach-Object {
                $binPath = Join-Path $_.FullName "bin"
                if (Test-Path $binPath) {
                    Get-ChildItem $binPath -Directory | ForEach-Object {
                        Join-Path $_.FullName "x64\$ToolName"
                    } | Where-Object { Test-Path $_ }
                }
            } | Select-Object -First 1

        if ($candidate) {
            return $candidate
        }
    }

    return $null
}

function Update-PackageManifestVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Version
    )
    [xml]$xml = Get-Content $Path
    $xml.Package.Identity.Version = $Version
    $xml.Save($Path)
}

function New-IdentityPri {
    param(
        [Parameter(Mandatory)]
        [string]$MakePriPath,
        [Parameter(Mandatory)]
        [string]$StagingDir,
        [Parameter(Mandatory)]
        [string]$OutputDir
    )
    Write-Host "Generating resources.pri..."
    $configPath = Join-Path $StagingDir "priconfig.xml"
    & $MakePriPath createconfig /cf $configPath /dq en-US /pv 10.0.0 /o | Out-Null
    
    [xml]$priConfig = Get-Content $configPath
    $packagingNode = $priConfig.SelectSingleNode("//packaging")
    if ($packagingNode) {
        $packagingNode.ParentNode.RemoveChild($packagingNode) | Out-Null
        $priConfig.Save($configPath)
    }

    & $MakePriPath new /pr $StagingDir /cf $configPath /of (Join-Path $OutputDir "resources.pri") /o /v | Out-Null
    Remove-Item $configPath -ErrorAction SilentlyContinue
}

function Write-IdentityImages {
    param(
        [Parameter(Mandatory)]
        [string]$DestinationDir
    )

    $imagesDestination = Join-Path $DestinationDir "Images"
    New-Item -ItemType Directory -Path $imagesDestination -Force | Out-Null
    Copy-Item "LenovoLegionToolkit.LampArray\Images\*" -Destination $imagesDestination -Recurse -Force

    $manifestLogos = @("Square150x150Logo", "Square44x44Logo", "Wide310x150Logo", "SplashScreen", "StoreLogo")
    foreach ($logoName in $manifestLogos) {
        $neutralPath = Join-Path $imagesDestination "$logoName.png"
        if (-not (Test-Path $neutralPath)) {
            $variant = Get-ChildItem -Path $imagesDestination -Filter "$logoName.targetsize-44_altform-unplated.png" -Recurse | Select-Object -First 1
            if (-not $variant) {
                $variant = Get-ChildItem -Path $imagesDestination -Filter "$logoName.scale-200.png" -Recurse | Select-Object -First 1
            }
            
            if ($variant) {
                Copy-Item $variant.FullName -Destination $neutralPath -Force
            }
        }
    }
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
    Write-IdentityImages -DestinationDir $DestinationDir
    New-Item -ItemType Directory -Path (Join-Path $DestinationDir "public") -Force | Out-Null

    Copy-Item $ManifestSource -Destination $manifestDestination -Force
    Update-PackageManifestVersion -Path $manifestDestination -Version $ResolvedVersion

    $priSource = Join-Path "LenovoLegionToolkit.LampArray" "resources.pri"
    if (Test-Path $priSource) {
        Copy-Item $priSource -Destination (Join-Path $DestinationDir "resources.pri") -Force
    }

    if (-not (Test-Path "LenovoLegionToolkit.cer")) {
        throw "Public certificate not found: LenovoLegionToolkit.cer"
    }

    Copy-Item "LenovoLegionToolkit.cer" -Destination (Join-Path $DestinationDir "LenovoLegionToolkit.cer") -Force
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
$makePri = Get-SdkToolPath -ToolName "makepri.exe"

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}

if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

Remove-Item (Join-Path $resolvedOutputDir "resources*.pri") -ErrorAction SilentlyContinue

if ($UseManifest -or -not $makeAppx -or -not $signTool) {
    if ($env:CI -eq "true" -and -not $UseManifest) {
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
    
    if ($makePri -and (Test-Path $makePri)) {
        Write-Host "Generating resources.pri for fallback..."
        $priStaging = Join-Path $resolvedOutputDir "pri_staging"
        New-Item -ItemType Directory -Path $priStaging -Force | Out-Null
        Copy-Item (Join-Path $resolvedOutputDir "AppxManifest.xml") -Destination $priStaging -Force
        Copy-Item (Join-Path $resolvedOutputDir "Images") -Destination $priStaging -Recurse -Force
        New-Item -ItemType Directory -Path (Join-Path $priStaging "public") -Force | Out-Null
        
        New-IdentityPri -MakePriPath $makePri -StagingDir $priStaging -OutputDir $resolvedOutputDir
        Remove-Item $priStaging -Recurse -Force
    }

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
Write-IdentityImages -DestinationDir $stagingDir
New-Item -ItemType Directory -Path (Join-Path $stagingDir "public") -Force | Out-Null

Copy-Item $manifestSource -Destination $manifestDestination -Force
Update-PackageManifestVersion -Path $manifestDestination -Version $resolvedVersion

if ($makePri -and (Test-Path $makePri)) {
    New-IdentityPri -MakePriPath $makePri -StagingDir $stagingDir -OutputDir $resolvedOutputDir
    Copy-Item (Join-Path $resolvedOutputDir "resources.pri") -Destination (Join-Path $stagingDir "resources.pri") -Force
}

& $makeAppx pack /o /d $stagingDir /nv /p $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE"
}

& $signTool sign /fd SHA256 /f $PfxPath /p $Password $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path "LenovoLegionToolkit.cer")) {
    throw "Public certificate not found: LenovoLegionToolkit.cer"
}

Copy-Item "LenovoLegionToolkit.cer" -Destination (Join-Path $resolvedOutputDir "LenovoLegionToolkit.cer") -Force

Copy-Item $manifestDestination -Destination (Join-Path $resolvedOutputDir "AppxManifest.xml") -Force
$finalImagesDestination = Join-Path $resolvedOutputDir "Images"
New-Item -ItemType Directory -Path $finalImagesDestination -Force | Out-Null
Copy-Item (Join-Path $stagingDir "Images\*") -Destination $finalImagesDestination -Recurse -Force

Remove-Item $stagingDir -Recurse -Force
Write-Host "Built identity package: $msixPath"