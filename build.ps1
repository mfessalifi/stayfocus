# Builds bin\StayFocusNotes.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
# Nothing to install. Run:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'The .NET Framework 4 C# compiler (csc.exe) was not found.' }

$icon = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-icon.ps1') | Out-Null }

$outDir = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $outDir | Out-Null
$exe = Join-Path $outDir 'StayFocusNotes.exe'

& $csc /nologo /target:winexe /optimize+ /codepage:65001 `
    "/out:$exe" `
    "/win32manifest:$(Join-Path $root 'app.manifest')" `
    "/win32icon:$icon" `
    "/resource:$icon,app.ico" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.Runtime.Serialization.dll /r:System.Xml.dll `
    (Join-Path $root 'src\*.cs')

if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit code $LASTEXITCODE)." }
"Built $exe"
