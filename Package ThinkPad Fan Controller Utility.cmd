@echo off
rem Builds dist\LibraryFan and dist\LibraryFan-try.zip. No LibraryFan.exe.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0packaging\package.ps1"
if errorlevel 1 (
  pause
  exit /b 1
)
