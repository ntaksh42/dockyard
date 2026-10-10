# Installs the signed package as a user would, starts it, updates it to a newer version and uninstalls it, failing
# on the first step that goes wrong.
#   pwsh installer/test.ps1
# Needs the signing certificate trusted as in README (Cert:\LocalMachine\TrustedPeople), and no Dockyard package
# installed or registered: the test package has the same identity.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$msix = Join-Path $root 'artifacts/msix/Dockyard.msix'
$runtime = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64' ? 'arm64' : 'x64'

function Step($name, [scriptblock]$check) {
    if (-not (& $check)) { throw "FAIL: $name" }
    Write-Host "ok: $name"
}

if (Get-AppxPackage Dockyard) { throw 'A Dockyard package is installed or registered: remove it first (Get-AppxPackage Dockyard | Remove-AppxPackage).' }

# Built and copied elsewhere: the installed package must not depend on the build folder.
function Build($version) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Version $version -Runtime $runtime | Out-Null
    $copy = Join-Path ([IO.Path]::GetTempPath()) "Dockyard-$version.msix"
    Copy-Item $msix $copy -Force
    $copy
}
$first = $second = $null
try {
    $first = Build '0.0.1.0'
    $cert = (Get-PfxCertificate (Join-Path $root 'artifacts/msix/Dockyard.cer')).Thumbprint
    if (-not (Get-ChildItem Cert:\LocalMachine\TrustedPeople, Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $cert)) {
        throw "The signing certificate $cert is not trusted: import artifacts\msix\Dockyard.cer as in README."
    }

    Add-AppxPackage $first
    $package = Get-AppxPackage Dockyard
    Step 'installs with the version and architecture built' { $package.Version -eq '0.0.1.0' -and "$($package.Architecture)" -eq $runtime }
    Step 'installs into WindowsApps, not the build folder' { $package.InstallLocation -like '*\WindowsApps\*' }
    Step 'the start menu lists it' { Get-StartApps | Where-Object AppID -eq "$($package.PackageFamilyName)!Dockyard" }
    Step 'the exe carries the package version' { (Get-Item (Join-Path $package.InstallLocation 'Dockyard.dll')).VersionInfo.FileVersion -eq '0.0.1.0' }

    # Only one Dockyard runs per user: with one running already, the packaged one would exit at once.
    if (-not (Get-Process Dockyard -ErrorAction SilentlyContinue)) {
        Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!Dockyard"
        $app = $null
        foreach ($i in 1..50) {
            $app = Get-Process Dockyard -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$($package.InstallLocation)*" } | Select-Object -First 1
            if ($app -and $app.MainWindowHandle -ne 0) { break }
            Start-Sleep -Milliseconds 200
        }
        Step 'starts from the package and opens a group frame' { $app -and $app.MainWindowTitle -like '`[*' }
        Stop-Process -Id $app.Id
        $app.WaitForExit(5000) | Out-Null
    } else {
        Write-Host 'skipped: starting it (another Dockyard is running; quit it to include this step)'
    }

    $second = Build '0.0.2.0'
    Add-AppxPackage $second
    Step 'a newer version installs over it' { (Get-AppxPackage Dockyard).Version -eq '0.0.2.0' }

    Remove-AppxPackage (Get-AppxPackage Dockyard).PackageFullName
    Step 'uninstalls' { -not (Get-AppxPackage Dockyard) }
    Write-Host 'ALL PASSED'
} finally {
    # Whatever failed, leave nothing of the test behind: its Dockyard, its package and the copied packages.
    Get-Process Dockyard -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\WindowsApps\Dockyard_*' } | Stop-Process
    Get-AppxPackage Dockyard | Where-Object { $_.Version -like '0.0.*' } | Remove-AppxPackage
    @($first, $second) | Where-Object { $_ } | Remove-Item -Force -ErrorAction SilentlyContinue
}
