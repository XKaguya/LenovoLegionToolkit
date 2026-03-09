@echo off

IF "%1"=="" (
SET VERSION=2.31.0.0
) ELSE (
SET VERSION=%1
)

dotnet publish LenovoLegionToolkit.WPF -c Release -o BuildLLT /p:FileVersion=%VERSION% /p:Version=%VERSION% > build_release.log 2>&1 || (type build_release.log && exit /b)
dotnet publish LenovoLegionToolkit.SpectrumTester -c Release -o BuildLLT /p:FileVersion=%VERSION% /p:Version=%VERSION% >> build_release.log 2>&1 || (type build_release.log && exit /b)
dotnet publish LenovoLegionToolkit.Probe -c Release -o BuildLLT /p:FileVersion=%VERSION% /p:Version=%VERSION% >> build_release.log 2>&1 || (type build_release.log && exit /b)
dotnet publish LenovoLegionToolkit.CLI -c Release -o BuildLLT /p:FileVersion=%VERSION% /p:Version=%VERSION% >> build_release.log 2>&1 || (type build_release.log && exit /b)

rmdir /s /q BuildLLT\obj 2>nul
rmdir /s /q BuildLLT\bin 2>nul

echo Copying packaging files...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build_identity_package.ps1" -Version %VERSION% -OutputDir "BuildLLT" || exit /b

echo Registering package identity...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-AppxPackage -Name 'eef45acd-2cf3-4d7d-9d33-92f37c74cc31' | Remove-AppxPackage -ErrorAction SilentlyContinue"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Import-Certificate -FilePath '%~dp0BuildLLT\LenovoLegionToolkit.LampArray.cer' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'"
powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Test-Path '%~dp0BuildLLT\LenovoLegionToolkit.LampArray.msix') { Add-AppxPackage -Path '%~dp0BuildLLT\LenovoLegionToolkit.LampArray.msix' -ExternalLocation '%~dp0BuildLLT' } else { Add-AppxPackage -Register '%~dp0BuildLLT\AppxManifest.xml' -ExternalLocation '%~dp0BuildLLT' }" || exit /b

echo.
echo Build completed successfully!
