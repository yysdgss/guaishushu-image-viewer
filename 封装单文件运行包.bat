@echo off
setlocal
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "OUT=%ROOT%dist\single"

echo ============================================
echo  该怪叔叔看图 - 封装单文件运行包
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [错误] 未找到 dotnet，请先安装 .NET 10 Desktop SDK/Runtime。
  goto end
)

if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%OUT%" >nul

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%OUT%"
if errorlevel 1 (
  echo [错误] 发布失败。
  goto end
)

copy /Y "%OUT%\GuaishushuViewer.exe" "%ROOT%GuaishushuViewer.exe" >nul
echo.
echo [完成] 单文件程序：%ROOT%GuaishushuViewer.exe
echo [完成] 发布目录：%OUT%
echo.

:end
if /i not "%~1"=="--ci" pause
