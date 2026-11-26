@echo off
cd /d "%~dp0"
if not exist "app\EarSubtitle.exe" (
  echo Please run Build.cmd first.
  pause
  exit /b 1
)
start "" "%~dp0app\EarSubtitle.exe"
