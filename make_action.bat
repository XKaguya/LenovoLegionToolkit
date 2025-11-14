@echo off

SET "VERSION="

IF "%1"=="" (
    SET VERSION=0.0.1
) ELSE (
    SET "FIRST_CHAR=%1:~0,1%"
    IF /I "%FIRST_CHAR%"=="-" (
        ECHO Warning: Command line argument "%1" looks like a switch (e.g. -1/-4/). Using default version 0.0.1.
        SET VERSION=0.0.1
    ) ELSE IF /I "%FIRST_CHAR%"=="/" (
        ECHO Warning: Command line argument "%1" looks like a switch. Using default version 0.0.1.
        SET VERSION=0.0.1
    ) ELSE (
        SET VERSION=%~1
    )
)

IF NOT DEFINED VERSION SET VERSION=0.0.1
set "TIMESTAMP=%date:~0,4%-%date:~5,2%-%date:~8,2%" 

SET PATH=%PATH%;"C:\Program Files (x86)\Inno Setup 6"

dotnet publish LenovoLegionToolkit.WPF -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.SpectrumTester -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b
dotnet publish LenovoLegionToolkit.CLI -c release -o build /p:DebugType=None /p:FileVersion=%VERSION% /p:Version=%VERSION% || exit /b

iscc make_installer_action.iss "/DMyAppVersion=%VERSION%" "/DMyTimestamp=%TIMESTAMP%" || exit /b