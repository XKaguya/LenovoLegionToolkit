@echo off

IF "%1"=="" (
SET VERSION=0.0.1
) ELSE (
SET VERSION=%1
)

REM Use pure batch commands to get the current date in YYYY-MM-DD format
set "TIMESTAMP=%date:~0,4%-%date:~5,2%-%date:~8,2%"

SET PATH=%PATH%;"C:\Program Files (x86)\Inno Setup 6"

dotnet publish LenovoLegionToolkit.WPF -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.SpectrumTester -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.Probe -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.CLI -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b

iscc make_installer.iss /DMyAppVersion=%VERSION% /DMyTimestamp=%TIMESTAMP% || exit /b