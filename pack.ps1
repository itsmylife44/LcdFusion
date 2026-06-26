param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# 1) compile
& "$root\LcdFusion\build.ps1"

# 2) stage the portable payload
$bin = Join-Path $root 'LcdFusion\bin'
$stageParent = Join-Path $env:TEMP 'lcdpack'
$stage = Join-Path $stageParent "LcdFusion-$Version-portable"
if (Test-Path $stageParent) { Remove-Item $stageParent -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'licenses') | Out-Null

foreach ($f in 'LcdFusion.exe','LibreHardwareMonitorLib.dll','HidSharp.dll','LibUsbDotNet.LibUsbDotNet.dll') {
    Copy-Item (Join-Path $bin $f) $stage
}
Copy-Item (Join-Path $root 'LcdFusion\app.ico') $stage
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'LcdFusion\THIRD_PARTY_NOTICES.md') $stage
Copy-Item (Join-Path $root 'LcdFusion\licenses\*') (Join-Path $stage 'licenses')

# 3) zip
$zip = Join-Path $root "LcdFusion-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageParent, $zip)
Write-Output $zip
