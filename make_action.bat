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
copy "LenovoLegionToolkit.LampArray\Package.appxmanifest" "build\AppxManifest.xml" /y >nul
if exist "LenovoLegionToolkit.LampArray.cer" copy "LenovoLegionToolkit.LampArray.cer" "build\LenovoLegionToolkit.LampArray.cer" /y >nul
xcopy "LenovoLegionToolkit.LampArray\Images" "build\Images" /s /e /i /y >nul

iscc make_installer_action.iss /DMyAppVersion=%VERSION% || exit /b