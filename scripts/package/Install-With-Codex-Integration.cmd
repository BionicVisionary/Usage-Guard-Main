@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-User.ps1" -SourceDirectory "%~dp0app" -SkillSourceDirectory "%~dp0skill" -InstallCodexIntegration -LaunchAfterInstall
if errorlevel 1 (
  echo.
  echo Usage Guard and Codex integration were not installed. Review the safe error above.
  pause
  exit /b 1
)
echo.
echo Usage Guard and the optional Codex task integration were installed for this Windows user.
pause
