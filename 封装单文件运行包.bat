@echo off
setlocal
rem Fixed script: the version is read from the source project on every run.
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "OUT=%ROOT%artifacts\single"
set "VERSION="
for /f "tokens=3 delims=<>" %%V in ('findstr /i /c:"<Version>" "%PROJ%"') do set "VERSION=%%V"
if not defined VERSION (
  echo [错误] 无法从 WBImageViewer.csproj 读取版本号。
  goto end
)

echo ============================================
echo  怪叔叔看图 - 生成单文件运行包
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

copy /Y "%OUT%\GuaishushuViewer.exe" "%OUT%\GuaishushuViewer-v%VERSION%.exe" >nul
echo [OK] versioned exe: %OUT%\GuaishushuViewer-v%VERSION%.exe
echo [目录] 发布目录：%OUT%
echo.

:end
if /i not "%~1"=="--ci" pause
