@echo off
rem Guided hardware test of every setting CompxTray can write. Restores your settings at the end.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-writes.ps1"
echo.
pause
