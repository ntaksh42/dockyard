# Builds a signed artifacts/msix/Dockyard.msix.
#   pwsh installer/build.ps1 [-Version 1.0.0.0] [-Runtime arm64|x64]
# The package is built for this PC's processor unless -Runtime says otherwise: an x64 package does install on
# ARM64, but then runs under emulation, slower and heavier.
# With -Dev, skips packing and signing and registers the layout folder in place instead (needs Developer Mode).
#   pwsh installer/build.ps1 -Dev
param([string]$Version = '1.0.0.0', [ValidateSet('x64', 'arm64')][string]$Runtime, [switch]$Dev)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'artifacts/msix'
$layout = Join-Path $out 'layout'
$msix = Join-Path $out 'Dockyard.msix'
$subject = 'CN=Dockyard Dev'
# The OS's own architecture: $env:PROCESSOR_ARCHITECTURE reports AMD64 inside an emulated x64 shell.
if (-not $Runtime) { $Runtime = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64' ? 'arm64' : 'x64' }

$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\makeappx.exe' |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk) { throw 'makeappx.exe not found. Install the Windows SDK.' }
$makeappx = $sdk.FullName
$signtool = Join-Path $sdk.DirectoryName 'signtool.exe'
$makepri = Join-Path $sdk.DirectoryName 'makepri.exe'

# A registered layout runs from its folder, so unregister it before the folder is rebuilt: with -Dev any Dockyard
# (the same identity cannot be installed twice), otherwise only one registered from this very folder.
Get-AppxPackage Dockyard | Where-Object { $Dev -or $_.InstallLocation -eq [IO.Path]::GetFullPath($layout) } | Remove-AppxPackage

# 1. The package layout: the published app plus the manifest and logos at its root.
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src/Dockyard.App') -c Release -r "win-$Runtime" --self-contained false -o $layout
if ($LASTEXITCODE) { throw 'dotnet publish failed.' }
(Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw).Replace('$Version$', $Version).Replace('$Architecture$', $Runtime) |
    Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding utf8

# The logos come from the PNG entries of the app icon, by pixel size (0 in the directory means 256).
Add-Type -AssemblyName System.Drawing
$ico = [IO.File]::ReadAllBytes((Join-Path $root 'src/Dockyard.App/Assets/Dockyard.ico'))
$entries = @{}
foreach ($i in 0..([BitConverter]::ToUInt16($ico, 4) - 1)) {
    $at = 6 + 16 * $i
    $entries[$ico[$at] -eq 0 ? 256 : [int]$ico[$at]] = $ico[[BitConverter]::ToInt32($ico, $at + 12)..([BitConverter]::ToInt32($ico, $at + 12) + [BitConverter]::ToInt32($ico, $at + 8) - 1)]
}
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
function Save-Logo([string]$name, [int]$size) {
    $path = Join-Path $assets $name
    # Sizes the icon has are copied as drawn (the small ones are drawn on the pixel grid); others are scaled from 256.
    if ($entries.ContainsKey($size)) { [IO.File]::WriteAllBytes($path, [byte[]]$entries[$size]); return }
    $source = [System.Drawing.Image]::FromStream([IO.MemoryStream]::new([byte[]]$entries[256]))
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($source, 0, 0, $size, $size)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose(); $source.Dispose()
}
Save-Logo 'StoreLogo.png' 50
Save-Logo 'Square150x150Logo.png' 150
Save-Logo 'Square44x44Logo.png' 44
# The taskbar, Start and Alt+Tab ask for these exact sizes. "unplated" draws the icon as it is, since it brings its
# own tile, instead of on an accent-coloured plate.
foreach ($size in 16, 20, 24, 32, 40, 48, 64, 256) {
    Save-Logo "Square44x44Logo.targetsize-$size.png" $size
    Save-Logo "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size
}

# resources.pri tells Windows which of the qualified logo files above to use where.
$priConfig = Join-Path $out 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri createconfig failed.' }
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri new failed.' }

if ($Dev) {
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host "Registered $layout"
    return
}

# 2. Pack the layout into a single .msix.
& $makeappx pack /o /d $layout /p $msix
if ($LASTEXITCODE) { throw 'makeappx failed.' }

# 3. Sign it. Windows refuses to install an unsigned package, so use a self-signed certificate kept in the user's store.
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature -FriendlyName 'Dockyard MSIX signing' `
        -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE) { throw 'signtool failed.' }
Export-Certificate -Cert $cert -FilePath (Join-Path $out 'Dockyard.cer') | Out-Null

Write-Host "Built $msix ($Runtime)"
