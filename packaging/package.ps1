$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\LibraryFan\LibraryFan.csproj'
$out = Join-Path $root 'dist\LibraryFan'
$zip = Join-Path $root 'dist\LibraryFan-try.zip'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw "dotnet.exe was not found at $dotnet"
}

if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}
New-Item -ItemType Directory -Path $out | Out-Null

$build = Join-Path $root 'dist\_build\'
& $dotnet publish $project -c Release -o $out -p:DebugType=none -p:DebugSymbols=false -p:OutputPath=$build -p:AppendTargetFrameworkToOutputPath=false
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Get-ChildItem $out -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.xml' } | Remove-Item -Force

Copy-Item (Join-Path $root 'third_party\pawnio\COPYING') (Join-Path $out 'COPYING')
Copy-Item (Join-Path $root 'hardware-profile.example.json') (Join-Path $out 'hardware-profile.example.json')
Copy-Item (Join-Path $PSScriptRoot 'Open ThinkPad Fan Controller Utility.cmd') (Join-Path $out 'Open ThinkPad Fan Controller Utility.cmd')
Copy-Item (Join-Path $PSScriptRoot 'Read this first.txt') (Join-Path $out 'Read this first.txt')

$required = @(
    'LibraryFan.dll',
    'LibraryFan.Core.dll',
    'LibraryFan.runtimeconfig.json',
    'library-curve.json',
    'LpcACPIEC.bin',
    'COPYING',
    'hardware-profile.example.json',
    'Open ThinkPad Fan Controller Utility.cmd',
    'Read this first.txt'
)
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $out $name))) {
        throw "The package is missing $name"
    }
}
if (Test-Path (Join-Path $out 'LibraryFan.exe')) {
    throw 'The package contains LibraryFan.exe. Smart App Control blocks that unsigned host.'
}
if (Test-Path (Join-Path $out 'hardware-profile.json')) {
    throw 'hardware-profile.json would replace the built-in map. Ship the example under its example name.'
}

& $dotnet exec (Join-Path $out 'LibraryFan.dll') --simulate --self-test
if ($LASTEXITCODE -ne 0) {
    throw "The packaged self-test exited with $LASTEXITCODE"
}
Remove-Item (Join-Path $out 'self-test.log') -ErrorAction SilentlyContinue

if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path $out -DestinationPath $zip
$setupProject = Join-Path $root 'src\LibraryFan.Setup\LibraryFan.Setup.csproj'
$setupOut = Join-Path $root 'dist\_setup'
if (Test-Path $setupOut) {
    Remove-Item $setupOut -Recurse -Force
}
& $dotnet publish $setupProject -c Release -r win-x64 --self-contained true -o $setupOut -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
Copy-Item (Join-Path $setupOut 'LibraryFanSetup.exe') (Join-Path $root 'dist\LibraryFanSetup.exe') -Force
Remove-Item $setupOut -Recurse -Force

$check = Join-Path $env:TEMP ("library-fan-install-" + [guid]::NewGuid().ToString('N'))
$install = Start-Process -FilePath (Join-Path $root 'dist\LibraryFanSetup.exe') -ArgumentList @('--from', $zip, '--into', $check) -Wait -PassThru
if ($install.ExitCode -ne 0) {
    throw "The installer did not accept the GitHub package."
}
if (-not (Test-Path (Join-Path $check 'LibraryFan.dll'))) {
    throw "The installer did not write LibraryFan.dll."
}
if (Test-Path (Join-Path $check 'LibraryFan.exe')) {
    throw "The installer wrote LibraryFan.exe."
}
Remove-Item $check -Recurse -Force

if (Test-Path $build) {
    Remove-Item $build -Recurse -Force
}
Write-Output "Package: $out"
Write-Output "Zip: $zip"
Write-Output "Installer: $(Join-Path $root 'dist\LibraryFanSetup.exe')"
