@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-User.ps1" -SourceDirectory "%~dp0app" -LaunchAfterInstall
if errorlevel 1 (
  echo.
  echo Usage Guard was not installed. Review the safe error above.
  pause
  exit /b 1
)
echo.
echo Usage Guard was installed for this Windows user.
pause
