@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\diagnostics.ps1"
if errorlevel 1 pause
