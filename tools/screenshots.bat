@echo off
rem Saves one PNG per tab of the settings window to docs\screenshots (reads the connected mouse).
rem Add --demo to use sample data instead:  tools\screenshots.bat --demo
"%~dp0..\bin\CompxTray.exe" --screenshot "%~dp0..\docs\screenshots" %1
