@echo off
rem ThinkPad Fan Controller Utility has no exe of its own. The signed dotnet.exe hosts LibraryFan.dll.
rem The project was formerly called Library Fan.
setlocal
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "DLL=%~dp0LibraryFan.dll"
if not exist "%DOTNET%" (
  echo Install the .NET 8 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8
  echo Choose Desktop Runtime, x64. The SDK is not required.
  pause
  exit /b 1
)
"%DOTNET%" --list-runtimes | findstr /c:"Microsoft.WindowsDesktop.App 8." >nul
if errorlevel 1 (
  echo The .NET 8 Desktop Runtime is not installed.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/8
  echo Choose Desktop Runtime, x64.
  pause
  exit /b 1
)
if not exist "%DLL%" (
  echo LibraryFan.dll is missing from this folder.
  pause
  exit /b 1
)
start "" "%DOTNET%" exec "%DLL%" %*
