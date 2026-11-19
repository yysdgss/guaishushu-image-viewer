@echo off
setlocal
chcp 65001 >nul
rem Fixed script: version comes from the source project. Both exe names are
rem discovered from publish directories, keeping this .bat pure ASCII while the
rem actual product and installer exe names are Chinese.
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "SETUP_PROJ=%ROOT%src\WBInstaller\WBInstaller.csproj"
set "BUILD=%ROOT%artifacts"
set "STAGE=%BUILD%\setup-stage"
set "INSTALLER_STAGE=%BUILD%\installer-stage"
set "OUT=%BUILD%\setup"
set "VERSION="
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION (
  echo [ERROR] Cannot read Version from WBImageViewer.csproj
  goto end
)

echo ============================================
echo  Guaishushu Image Viewer - Windows installer
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet not found. Install .NET 10 Desktop SDK/Runtime first.
  goto end
)

if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%INSTALLER_STAGE%" rd /s /q "%INSTALLER_STAGE%"
if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%STAGE%" >nul
mkdir "%INSTALLER_STAGE%" >nul
mkdir "%OUT%" >nul

echo [1/2] Publishing viewer into installer staging...
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
if errorlevel 1 (
  echo [ERROR] Viewer publish failed.
  goto cleanup
)
set "BUILT_EXE="
for %%F in ("%STAGE%\*.exe") do set "BUILT_EXE=%%~nxF"
if not defined BUILT_EXE (
  echo [ERROR] No viewer exe found in staging.
  goto cleanup
)
for %%F in ("%BUILT_EXE%") do set "BASE_NAME=%%~nF"
set "FINAL_EXE=%OUT%\%BASE_NAME%-v%VERSION%-Setup.exe"

echo [2/2] Building Windows installer...
dotnet publish "%SETUP_PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:Version=%VERSION% -o "%INSTALLER_STAGE%"
if errorlevel 1 (
  echo [ERROR] Installer publish failed.
  goto cleanup
)
set "SETUP_EXE="
for %%F in ("%INSTALLER_STAGE%\*.exe") do set "SETUP_EXE=%%~nxF"
if not defined SETUP_EXE (
  echo [ERROR] No installer exe found.
  goto cleanup
)

copy /Y "%INSTALLER_STAGE%\%SETUP_EXE%" "%FINAL_EXE%" >nul
echo [OK] Installer package: %FINAL_EXE%
echo [NOTE] Exactly one versioned installer exe is produced in this folder.
echo.

:cleanup
if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%INSTALLER_STAGE%" rd /s /q "%INSTALLER_STAGE%"

:end
if /i not "%~1"=="--ci" pause
