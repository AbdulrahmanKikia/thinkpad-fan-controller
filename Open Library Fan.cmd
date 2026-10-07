@echo off
rem Library Fan ships as LibraryFan.dll with no exe of its own, because Smart App Control
rem blocks an unsigned apphost on this machine. The signed dotnet.exe hosts it instead.
setlocal
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "DLL=%~dp0src\LibraryFan\bin\Release\net8.0-windows\LibraryFan.dll"
if not exist "%DOTNET%" (
  echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8 first.
  pause
  exit /b 1
)
if not exist "%DLL%" (
  echo Building Library Fan...
  "%DOTNET%" build "%~dp0src\LibraryFan\LibraryFan.csproj" -c Release -v q
  if errorlevel 1 (
    pause
    exit /b 1
  )
)
start "" "%DOTNET%" exec "%DLL%" %*
