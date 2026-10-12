@echo off
rem ThinkPad Fan Controller Utility ships as LibraryFan.dll with no exe of its own,
rem because Smart App Control blocks an unsigned apphost on this machine.
rem The project was formerly called Library Fan. The signed dotnet.exe hosts the DLL.
setlocal
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "DLL=%~dp0src\LibraryFan\bin\Release\net8.0-windows\LibraryFan.dll"
if not exist "%DOTNET%" (
  echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8 first.
  pause
  exit /b 1
)
if not exist "%DLL%" (
  echo Building ThinkPad Fan Controller Utility...
  "%DOTNET%" build "%~dp0src\LibraryFan\LibraryFan.csproj" -c Release -v q
  if errorlevel 1 (
    pause
    exit /b 1
  )
)
start "" "%DOTNET%" exec "%DLL%" %*
