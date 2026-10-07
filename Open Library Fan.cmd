@echo off
set DOTNET=%ProgramFiles%\dotnet\dotnet.exe
set DLL=%~dp0src\LibraryFan\bin\Debug\net8.0-windows\LibraryFan.dll
if not exist "%DLL%" (
  "%DOTNET%" build "%~dp0src\LibraryFan\LibraryFan.csproj" -v q
)
start "" "%DOTNET%" exec "%DLL%" %*
