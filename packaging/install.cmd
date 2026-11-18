@echo off
setlocal
set "APP=GuaishushuViewer.exe"
set "DEST=%LOCALAPPDATA%\GuaishushuViewer"
set "PROGID=GuaishushuViewer.Image"

rem --- Version comes from the executable produced by the build; this script is version-agnostic ---
set "VERSION="

if not exist "%DEST%" mkdir "%DEST%" >nul
copy /Y "%~dp0%APP%" "%DEST%\%APP%" >nul
if errorlevel 1 (
  echo [错误] 复制程序失败。
  pause
  exit /b 1
)

set "RAW_VERSION="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%DEST%\%APP%').VersionInfo.ProductVersion"`) do set "RAW_VERSION=%%V"
for /f "tokens=1 delims=+" %%V in ("%RAW_VERSION%") do set "VERSION=%%V"
if not defined VERSION set "VERSION=unknown"

echo 正在安装 怪叔叔看图 %VERSION% ...

rem --- Register a ProgID and OpenWith candidate for all supported picture formats ---
reg add "HKCU\Software\Classes\%PROGID%" /ve /d "怪叔叔看图" /f >nul
reg add "HKCU\Software\Classes\%PROGID%\DefaultIcon" /ve /d "\"%DEST%\%APP%,0\"" /f >nul
reg add "HKCU\Software\Classes\%PROGID%\shell\open\command" /ve /d "\"%DEST%\%APP%\" \"%%1\"" /f >nul

for %%E in (.png .jpg .jpeg .jfif .jpe .bmp .dib .gif .webp .tif .tiff .ico .heic .heif .avif .jxl .jp2 .j2k .jpf .jpx .wdp .hdp .exif .tga .pcx .pbm .pgm .ppm .pnm .sgi .ras .svg .svgz .emf .wmf .pdf .psd .ai .eps .xmind .mm .drawio .vsdx .vsd .vdw .vst .graphml) do (
  reg add "HKCU\Software\Classes\%%E" /ve /d "%PROGID%" /f >nul
  reg add "HKCU\Software\Classes\%%E\OpenWithProgids" /v "%PROGID%" /d "" /f >nul
  reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\%%E\UserChoice" /f >nul 2>&1
  reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\%%E\UserChoiceLatest" /f >nul 2>&1
)

rem --- Start menu and desktop shortcuts ---
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w=New-Object -ComObject WScript.Shell;" ^
  "$d=[Environment]::GetFolderPath('Desktop')+'\\怪叔叔看图.lnk';" ^
  "$s=$w.CreateShortcut($d);$s.TargetPath='%DEST%\\%APP%';$s.Save();" ^
  "$p=[Environment]::GetFolderPath('Programs')+'\\怪叔叔看图.lnk';" ^
  "$s=$w.CreateShortcut($p);$s.TargetPath='%DEST%\\%APP%';$s.Save();" >nul 2>&1

rem --- Add/remove program entry ---
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /ve /f >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /v DisplayName /d "怪叔叔看图" /f >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /v DisplayVersion /d "%VERSION%" /f >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /v UninstallString /d "\"%DEST%\%APP%\" --uninstall" /f >nul

rundll32.exe shell32.dll,SHChangeNotify 0x08000000,0,0,0 >nul 2>&1

echo.
echo 安装完成。正在打开 Windows 的默认程序确认窗口：
echo 请在弹出的窗口中选择“怪叔叔看图”，并勾选“始终使用此应用打开”。
start "" "%DEST%\%APP%" --set-default

echo.
timeout /t 3 >nul 2>&1
