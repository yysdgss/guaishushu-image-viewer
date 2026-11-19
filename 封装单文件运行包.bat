@echo off
setlocal
chcp 65001 >nul
rem Fixed script: version comes from the source project. The output exe name is
rem discovered from the publish directory, so this .bat stays pure ASCII and can
rem never produce mojibake even though the product name itself is Chinese.
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "BUILD=%ROOT%artifacts"
set "STAGE=%BUILD%\single-stage"
set "OUT=%BUILD%\single"
set "VERSION="
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION (
  echo [ERROR] Cannot read Version from WBImageViewer.csproj
  goto end
)

echo ============================================
echo  Guaishushu Image Viewer - single-file build
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet not found. Install .NET 10 Desktop SDK/Runtime first.
  goto end
)

if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%STAGE%" >nul
mkdir "%OUT%" >nul

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
if errorlevel 1 (
  echo [ERROR] Publish failed.
  goto cleanup
)

rem Discover the published exe name (it is the Chinese product name) without
rem embedding any non-ASCII byte in this script.
set "BUILT_EXE="
for %%F in ("%STAGE%\*.exe") do set "BUILT_EXE=%%~nxF"
if not defined BUILT_EXE (
  echo [ERROR] No exe found in publish output.
  goto cleanup
)
for %%F in ("%BUILT_EXE%") do set "BASE_NAME=%%~nF"

copy /Y "%STAGE%\%BUILT_EXE%" "%OUT%\%BASE_NAME%-v%VERSION%.exe" >nul
echo [OK] Single-file package: %OUT%\%BASE_NAME%-v%VERSION%.exe
echo [NOTE] Exactly one versioned exe is produced in this folder.
echo.

:cleanup
if exist "%STAGE%" rd /s /q "%STAGE%"

:end
if /i not "%~1"=="--ci" pause
