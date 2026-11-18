@echo off
setlocal
rem Fixed script: the version is read from the source project on every run.
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
  echo [错误] 无法从 WBImageViewer.csproj 读取版本号。
  goto end
)
set "PACKAGE=%STAGE%\GuaishushuViewer-Setup.zip"
set "FINAL=%OUT%\GuaishushuViewer-v%VERSION%-Setup.zip"
set "STABLE=%OUT%\GuaishushuViewer-Setup.zip"
set "FINAL_EXE=%OUT%\GuaishushuViewer-v%VERSION%-Setup.exe"
set "STABLE_EXE=%OUT%\GuaishushuViewer-Setup.exe"

echo ============================================
echo  怪叔叔看图 - 生成 Windows 安装程序
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [错误] 未找到 dotnet，请先安装 .NET 10 Desktop SDK/Runtime。
  goto end
)

if exist "%STAGE%" rd /s /q "%STAGE%"
if not exist "%OUT%" mkdir "%OUT%" >nul
mkdir "%STAGE%" >nul

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
if errorlevel 1 (
  echo [错误] 发布失败。
  goto end
)
copy /Y "%ROOT%packaging\install.cmd" "%STAGE%\install.cmd" >nul

powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%PACKAGE%' -Force"
if errorlevel 1 (
  echo [错误] 压缩包生成失败。
  goto end
)

copy /Y "%PACKAGE%" "%FINAL%" >nul
copy /Y "%PACKAGE%" "%STABLE%" >nul
echo [OK] versioned setup zip: %FINAL%

if exist "%INSTALLER_STAGE%" rd /s /q "%INSTALLER_STAGE%"
mkdir "%INSTALLER_STAGE%" >nul

dotnet publish "%SETUP_PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:Version=%VERSION% -o "%INSTALLER_STAGE%"
if errorlevel 1 (
  echo [错误] 安装程序发布失败。
  goto end
)

copy /Y "%INSTALLER_STAGE%\GuaishushuViewerSetup.exe" "%FINAL_EXE%" >nul
copy /Y "%INSTALLER_STAGE%\GuaishushuViewerSetup.exe" "%STABLE_EXE%" >nul
echo [OK] versioned setup exe: %FINAL_EXE%

echo.
echo [完成] 安装包目录：%OUT%
echo [说明] 推荐运行 GuaishushuViewer-Setup.exe；zip 为兼容旧方式的免安装/注册包。
echo.

:end
if /i not "%~1"=="--ci" pause
