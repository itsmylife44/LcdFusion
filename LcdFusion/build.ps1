$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'bin'
$gac = 'C:\Windows\Microsoft.NET\assembly'
$presentationFramework = Join-Path $gac 'GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll'
$presentationCore = Join-Path $gac 'GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll'
$windowsBase = Join-Path $gac 'GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'
$systemXaml = Join-Path $gac 'GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll'
$libUsb = Join-Path (Split-Path -Parent $root) 'tools\libusbdotnet\2.2.29\lib\net45\LibUsbDotNet.LibUsbDotNet.dll'
$lhmDir = Join-Path (Split-Path -Parent $root) 'tools\librehardwaremonitor'
$lhm = Join-Path $lhmDir 'LibreHardwareMonitorLib.dll'
$hidSharp = Join-Path $lhmDir 'HidSharp.dll'
New-Item -ItemType Directory -Force -Path $out | Out-Null

& $csc /nologo /target:winexe /platform:x86 /optimize+ `
    /out:"$out\LcdFusion.exe" `
    /win32manifest:"$root\app.manifest" `
    /win32icon:"$root\app.ico" `
    /reference:"$presentationFramework" `
    /reference:"$presentationCore" `
    /reference:"$windowsBase" `
    /reference:"$systemXaml" `
    /reference:"System.Drawing.dll" `
    /reference:"System.Windows.Forms.dll" `
    /reference:"System.Management.dll" `
    /reference:"$libUsb" `
    /reference:"$lhm" `
    /reference:"$hidSharp" `
    "$root\Program.cs" `
    "$root\AssemblyInfo.cs" `
    "$root\Loc.cs" `
    "$root\ProductCatalog.cs" `
    "$root\ProfileService.cs" `
    "$root\AutoStartService.cs" `
    "$root\Theme.cs" `
    "$root\MainWindow.cs" `
    "$root\DeviceService.cs" `
    "$root\VendorService.cs" `
    "$root\SensorService.cs" `
    "$root\ContentEngine.cs" `
    "$root\ValkyrieDirectService.cs" `
    "$root\ThermalrightDirectService.cs" `
    "$root\..\protocol\ValkyrieHidInitializer.cs"

if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita: $LASTEXITCODE" }
Copy-Item -LiteralPath $libUsb -Destination (Join-Path $out 'LibUsbDotNet.LibUsbDotNet.dll') -Force
Copy-Item -LiteralPath $lhm -Destination (Join-Path $out 'LibreHardwareMonitorLib.dll') -Force
Copy-Item -LiteralPath $hidSharp -Destination (Join-Path $out 'HidSharp.dll') -Force
Write-Host "Creato $out\LcdFusion.exe"
