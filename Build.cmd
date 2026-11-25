@echo off
rem Builds YouTube CarPlay Sync and opens the new installer.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. See the messages above.
  pause
  exit /b 1
)
start "" "%~dp0dist\YouTube-CarPlay-Sync-Setup.exe"
