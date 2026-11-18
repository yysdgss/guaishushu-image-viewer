@echo off
setlocal
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "STAGE=%TEMP%\GuaishushuViewerSetup"
set "PACKAGE=%STAGE%\GuaishushuViewer-Setup.zip"
set "FINAL=%ROOT%dist\GuaishushuViewer-Setup.zip"

echo ============================================
echo  该怪叔叔看图 - 封装 Windows 安装包
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [错误] 未找到 dotnet，请先安装 .NET 10 Desktop SDK/Runtime。
  goto end
)

if exist "%STAGE%" rd /s /q "%STAGE%"
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

if not exist "%ROOT%dist" mkdir "%ROOT%dist" >nul
copy /Y "%PACKAGE%" "%FINAL%" >nul

echo.
echo [完成] 安装包：%FINAL%
echo [说明] 解压后运行 install.cmd，即可自动安装、创建快捷方式并注册图片关联。
echo.

:end
if /i not "%~1"=="--ci" pause
