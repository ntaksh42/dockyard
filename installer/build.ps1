# Builds a signed artifacts/msix/Dockyard.msix.
#   pwsh installer/build.ps1 [-Version 1.0.0.0]
# With -Dev, skips packing and signing and registers the layout folder in place instead (needs Developer Mode).
#   pwsh installer/build.ps1 -Dev
param([string]$Version = '1.0.0.0', [switch]$Dev)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'artifacts/msix'
$layout = Join-Path $out 'layout'
$msix = Join-Path $out 'Dockyard.msix'
$subject = 'CN=Dockyard Dev'

$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\makeappx.exe' |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk) { throw 'makeappx.exe not found. Install the Windows SDK.' }
$makeappx = $sdk.FullName
$signtool = Join-Path $sdk.DirectoryName 'signtool.exe'

# A registered layout runs from its folder, so unregister it before the folder is rebuilt.
if ($Dev) { Get-AppxPackage Dockyard | Remove-AppxPackage }

# 1. The package layout: the published app plus the manifest and logos at its root.
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src/Dockyard.App') -c Release -r win-x64 --self-contained false -o $layout
if ($LASTEXITCODE) { throw 'dotnet publish failed.' }
(Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw).Replace('$Version$', $Version) |
    Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding utf8

# The logos come from the 256px PNG entry in the app icon.
Add-Type -AssemblyName System.Drawing
$ico = [IO.File]::ReadAllBytes((Join-Path $root 'src/Dockyard.App/Assets/Dockyard.ico'))
$entry = (0..([BitConverter]::ToUInt16($ico, 4) - 1) | ForEach-Object { 6 + 16 * $_ } | Where-Object { $ico[$_] -eq 0 })[0]
$png = [IO.MemoryStream]::new($ico, [BitConverter]::ToInt32($ico, $entry + 12), [BitConverter]::ToInt32($ico, $entry + 8))
$source = [System.Drawing.Image]::FromStream($png)
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
foreach ($logo in @{ StoreLogo = 50; Square44x44Logo = 44; Square150x150Logo = 150 }.GetEnumerator()) {
    $bitmap = [System.Drawing.Bitmap]::new($logo.Value, $logo.Value)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($source, 0, 0, $logo.Value, $logo.Value)
    $bitmap.Save((Join-Path $layout "Assets/$($logo.Key).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose()
}
$source.Dispose()

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

Write-Host "Built $msix"
