@echo off
setlocal
rem Fixed script: the version is read from the source project on every run.
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "BUILD=%ROOT%artifacts"
set "STAGE=%BUILD%\setup-stage"
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

echo ============================================
echo  怪叔叔看图 - 生成 Windows 安装包
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
echo [OK] versioned setup: %FINAL%
echo [OK] stable setup: %STABLE%

echo.
echo [完成] 安装包目录：%OUT%
echo [说明] 解压后运行 install.cmd，安装结束会自动打开系统默认程序确认窗口。
echo.

:end
if /i not "%~1"=="--ci" pause
