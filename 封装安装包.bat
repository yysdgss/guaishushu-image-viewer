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
set "FINAL_EXE=%OUT%\GuaishushuViewer-v%VERSION%-Setup.exe"

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
if exist "%INSTALLER_STAGE%" rd /s /q "%INSTALLER_STAGE%"
if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%STAGE%" >nul
mkdir "%INSTALLER_STAGE%" >nul
mkdir "%OUT%" >nul

echo [1/2] 发布主程序到安装资源暂存区...
dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
if errorlevel 1 (
  echo [错误] 主程序发布失败。
  goto cleanup
)
if not exist "%STAGE%\GuaishushuViewer.exe" (
  echo [错误] 安装资源暂存区缺少 GuaishushuViewer.exe。
  goto cleanup
)

echo [2/2] 生成常规 Windows 安装程序...
dotnet publish "%SETUP_PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:Version=%VERSION% -o "%INSTALLER_STAGE%"
if errorlevel 1 (
  echo [错误] 安装程序发布失败。
  goto cleanup
)
if not exist "%INSTALLER_STAGE%\GuaishushuViewerSetup.exe" (
  echo [错误] 未找到生成的安装程序。
  goto cleanup
)

copy /Y "%INSTALLER_STAGE%\GuaishushuViewerSetup.exe" "%FINAL_EXE%" >nul
echo [OK] 安装包：%FINAL_EXE%
echo [说明] 输出目录仅保留这一个带版本号的 exe 安装程序。
echo.

:cleanup
if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%INSTALLER_STAGE%" rd /s /q "%INSTALLER_STAGE%"

:end
if /i not "%~1"=="--ci" pause
