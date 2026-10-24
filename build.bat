@echo off
rem Builds bin\CompxTray.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK or Visual Studio needed.   build.bat        build, then start the app
rem                                    build.bat --ci   build only (used by GitHub Actions)
setlocal
cd /d "%~dp0"
set "CI=0"
if /i "%~1"=="--ci" set "CI=1"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe was not found. CompxTray needs Windows with .NET Framework 4.x.
  if "%CI%"=="0" pause
  exit /b 1
)

if not exist bin mkdir bin
if "%CI%"=="0" (
  taskkill /im CompxTray.exe /f >nul 2>&1
  timeout /t 1 >nul
)

echo Building bin\CompxTray.exe ...
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:bin\CompxTray.exe /win32icon:assets\app.ico /win32manifest:assets\app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /recurse:src\*.cs > bin\build-log.txt 2>&1
if errorlevel 1 (
  type bin\build-log.txt
  echo.
  echo Build failed.
  if "%CI%"=="0" pause
  exit /b 1
)
type bin\build-log.txt
echo Built bin\CompxTray.exe

if "%CI%"=="0" start "" "%~dp0bin\CompxTray.exe" --open
exit /b 0
