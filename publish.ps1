<#
.SYNOPSIS
  Builds a release: runs the tests, publishes a self-contained single-file
  FolderDiskSize.exe for win-x64 and zips it.

.EXAMPLE
  .\publish.ps1                  # version from Directory.Build.props
  .\publish.ps1 -Version 0.2.0   # override the version for this build
#>
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'src\FolderDiskSize.App\FolderDiskSize.App.csproj'

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
if (-not $Version) { throw 'No version given and none found in Directory.Build.props.' }

$out = Join-Path $root "artifacts\FolderDiskSize-$Version-win-x64"
$zip = "$out.zip"

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'FolderDiskSize.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish $app -p:PublishProfile=win-x64 -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii

Write-Host ''
Write-Host "Version $Version"
Write-Host "Exe:     $(Join-Path $out 'FolderDiskSize.exe')"
Write-Host "Zip:     $zip"
Write-Host "SHA256:  $hash"
