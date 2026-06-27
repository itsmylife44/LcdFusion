param(
  [string]$Version = "1.0.0",
  [string]$ChangelogPath = ""
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'LcdFusion\LcdFusion.csproj'

# 1) publish (framework-dependent net48, x86)
$pub = Join-Path $env:TEMP 'lcdpublish'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish $proj -c Release -o $pub --nologo `
  /p:Version=$Version `
  /p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# 2) stage the portable payload (binaries + docs + licenses, no pdb)
$stageParent = Join-Path $env:TEMP 'lcdpack'
$stage = Join-Path $stageParent "LcdFusion-$Version-portable"
if (Test-Path $stageParent) { Remove-Item $stageParent -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'licenses') | Out-Null

Get-ChildItem $pub -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object { Copy-Item $_.FullName $stage }
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'LcdFusion\THIRD_PARTY_NOTICES.md') $stage
Copy-Item (Join-Path $root 'LcdFusion\licenses\*') (Join-Path $stage 'licenses')
if ($ChangelogPath) {
  $resolvedChangelog = Resolve-Path $ChangelogPath -ErrorAction Stop
  Copy-Item $resolvedChangelog (Join-Path $stage 'CHANGELOG.md')
}

# 3) zip
$zip = Join-Path $root "LcdFusion-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageParent, $zip)
Write-Output $zip
