@echo off
setlocal
rem Fixed script: the version is read from the source project on every run.
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\WBImageViewer\WBImageViewer.csproj"
set "BUILD=%ROOT%artifacts"
set "STAGE=%BUILD%\single-stage"
set "OUT=%BUILD%\single"
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

if exist "%STAGE%" rd /s /q "%STAGE%"
if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%STAGE%" >nul
mkdir "%OUT%" >nul

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%STAGE%"
if errorlevel 1 (
  echo [错误] 发布失败。
  goto cleanup
)
if not exist "%STAGE%\GuaishushuViewer.exe" (
  echo [错误] 发布目录中未找到 GuaishushuViewer.exe。
  goto cleanup
)

copy /Y "%STAGE%\GuaishushuViewer.exe" "%OUT%\GuaishushuViewer-v%VERSION%.exe" >nul
echo [OK] 单文件运行包：%OUT%\GuaishushuViewer-v%VERSION%.exe
echo [说明] 输出目录仅保留这一个带版本号的 exe。
echo.

:cleanup
if exist "%STAGE%" rd /s /q "%STAGE%"

:end
if /i not "%~1"=="--ci" pause
