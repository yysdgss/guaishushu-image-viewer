@echo off
setlocal
set "APP=GuaishushuViewer.exe"
set "DEST=%LOCALAPPDATA%\GuaishushuViewer"
set "PROGID=GuaishushuViewer.Image"

echo 正在安装 该怪叔叔看图 1.2.0 ...

if not exist "%DEST%" mkdir "%DEST%" >nul
copy /Y "%~dp0%APP%" "%DEST%\%APP%" >nul
if errorlevel 1 (
  echo [错误] 复制程序失败。
  pause
  exit /b 1
)

rem --- Register a ProgID and OpenWith candidate for all supported picture formats ---
reg add "HKCU\Software\Classes\%PROGID%" /ve /d "该怪叔叔看图" /f >nul
reg add "HKCU\Software\Classes\%PROGID%\DefaultIcon" /ve /d "\"%DEST%\%APP%,0\"" /f >nul
reg add "HKCU\Software\Classes\%PROGID%\shell\open\command" /ve /d "\"%DEST%\%APP%\" \"%%1\"" /f >nul

for %%E in (.png .jpg .jpeg .jfif .bmp .gif .webp .tif .tiff .ico) do (
  reg add "HKCU\Software\Classes\%%E" /ve /d "%PROGID%" /f >nul
  reg add "HKCU\Software\Classes\%%E\OpenWithProgids" /v "%PROGID%" /d "" /f >nul
  reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\%%E\UserChoice" /f >nul 2>&1
  reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\%%E\UserChoiceLatest" /f >nul 2>&1
)

rem --- Start menu and desktop shortcuts ---
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w=New-Object -ComObject WScript.Shell;" ^
  "$d=[Environment]::GetFolderPath('Desktop')+'\\该怪叔叔看图.lnk';" ^
  "$s=$w.CreateShortcut($d);$s.TargetPath='%DEST%\\%APP%';$s.Save();" ^
  "$p=[Environment]::GetFolderPath('Programs')+'\\该怪叔叔看图.lnk';" ^
  "$s=$w.CreateShortcut($p);$s.TargetPath='%DEST%\\%APP%';$s.Save();" >nul 2>&1

rem --- Add/remove program entry ---
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /ve /f >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /v DisplayName /d "该怪叔叔看图" /f >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /v DisplayVersion /d "1.2.0" /f >nul
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuaishushuViewer" /v UninstallString /d "\"%DEST%\%APP%\" --uninstall" /f >nul

rundll32.exe shell32.dll,SHChangeNotify 0x08000000,0,0,0 >nul 2>&1

echo.
echo 安装完成。正在打开 Windows 的默认程序确认窗口：
echo 请在弹出的窗口中选择“该怪叔叔看图”，并勾选“始终使用此应用打开”。
start "" "%DEST%\%APP%" --set-default

echo.
timeout /t 3 >nul 2>&1
