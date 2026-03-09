@echo off
echo Cleaning build artifacts...

powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-AppxPackage -Name 'eef45acd-2cf3-4d7d-9d33-92f37c74cc31' | Remove-AppxPackage -ErrorAction SilentlyContinue"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue | Where-Object { $_.Subject -match 'LenovoLegionToolkit' } | Remove-Item -Force -ErrorAction SilentlyContinue"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue | Where-Object { $_.Subject -match 'LenovoLegionToolkit' } | Remove-Item -Force -ErrorAction SilentlyContinue"

rmdir /s /q .vs
rmdir /s /q _ReSharper.Caches

rmdir /s /q build
rmdir /s /q BuildLLT
rmdir /s /q build_installer

rmdir /s /q LenovoLegionToolkit.CLI\bin
rmdir /s /q LenovoLegionToolkit.CLI\obj

rmdir /s /q LenovoLegionToolkit.Lib\bin
rmdir /s /q LenovoLegionToolkit.Lib\obj

rmdir /s /q LenovoLegionToolkit.Lib.Automation\bin
rmdir /s /q LenovoLegionToolkit.Lib.Automation\obj

rmdir /s /q LenovoLegionToolkit.Lib.CLI\bin
rmdir /s /q LenovoLegionToolkit.Lib.CLI\obj

rmdir /s /q LenovoLegionToolkit.Lib.Macro\bin
rmdir /s /q LenovoLegionToolkit.Lib.Macro\obj

rmdir /s /q LenovoLegionToolkit.WPF\bin
rmdir /s /q LenovoLegionToolkit.WPF\obj

rmdir /s /q LenovoLegionToolkit.SpectrumTester\bin
rmdir /s /q LenovoLegionToolkit.SpectrumTester\obj
