@echo off

IF "%1"=="" (
SET VERSION=0.0.1
) ELSE (
SET VERSION=%1
)

SET PATH=%PATH%;"C:\Program Files (x86)\Inno Setup 6"

dotnet publish LenovoLegionToolkit.WPF -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.SpectrumTester -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.Probe -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.CLI -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b

echo Copying packaging files...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\build_identity_package.ps1" -Version %VERSION% -OutputDir "build" || exit /b

iscc make_installer_action.iss /DMyAppVersion=%VERSION% || exit /b

echo Stamping installer executable...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\sign_installer.ps1" || exit /b