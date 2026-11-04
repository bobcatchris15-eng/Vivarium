@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0tools\dotnet10\dotnet.exe" (
    set "DOTNET_ROOT=%~dp0tools\dotnet10"
    set "PATH=%~dp0tools\dotnet10;%PATH%"
)
set "VIVARIUM_ENGINE=%~dp0tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe"
if defined VIVARIUM_GODOT set "VIVARIUM_ENGINE=%VIVARIUM_GODOT%"
if not exist "%VIVARIUM_ENGINE%" (
    echo Godot 4.7.1 .NET was not found. See README.md.
    goto failed
)
where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. See README.md.
    goto failed
)
dotnet build "%~dp0Vivarium.sln" --nologo -v minimal
if errorlevel 1 goto failed
if not exist "%~dp0game\.godot\uid_cache.bin" (
    "%VIVARIUM_ENGINE%" --headless --editor --path "%~dp0game" --import
    if errorlevel 1 goto failed
)
start "" "%VIVARIUM_ENGINE%" --path "%~dp0game" %*
exit /b 0
:failed
echo Vivarium could not be launched.
pause
exit /b 1
