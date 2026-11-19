<#
.SYNOPSIS
    Builds artifacts\BatteryChecker-Setup-<version>.exe: self-contained publish + Inno Setup.
.NOTES
    Needs Inno Setup 6 (winget install JRSoftware.InnoSetup). Version comes from the csproj <Version>.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\BattaryChecker\BattaryChecker.csproj'
$version = @(([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "No valid <Version> in $project`: '$version'" }
$publish = Join-Path $root 'artifacts\publish'

# Fresh folder: files left over from an older build would be packed into the installer.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
# One exe with .NET inside: runs on a clean Windows 10/11 x64 without the Desktop Runtime.
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$version" "/DSourceDir=$publish" (Join-Path $PSScriptRoot 'BattaryChecker.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Get-Item (Join-Path $root "artifacts\BatteryChecker-Setup-$version.exe")
