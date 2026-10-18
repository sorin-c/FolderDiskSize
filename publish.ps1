<#
.SYNOPSIS
  Builds a release: runs the tests, then publishes two builds of FolderDiskSize.exe for win-x64
  and zips each one:
    - standalone:       self-contained, runs on any 64-bit Windows PC (large, about 60 MB)
    - requires-dotnet:  small, needs the .NET 10 Desktop Runtime installed

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

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'FolderDiskSize.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

# Publish profile (src/FolderDiskSize.App/Properties/PublishProfiles) -> suffix of the output name.
$builds = [ordered]@{
    'win-x64'           = 'win-x64-standalone'
    'win-x64-framework' = 'win-x64-requires-dotnet'
}

$results = foreach ($name in $builds.Keys) {
    $out = Join-Path $root "artifacts\FolderDiskSize-$Version-$($builds[$name])"
    $zip = "$out.zip"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    if (Test-Path $zip) { Remove-Item $zip -Force }

    dotnet publish $app -p:PublishProfile=$name -p:Version=$Version -o $out
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for profile $name." }

    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii
    [pscustomobject]@{ Zip = Split-Path $zip -Leaf; MB = [math]::Round((Get-Item $zip).Length / 1MB, 1); SHA256 = $hash }
}

Write-Host ''
Write-Host "Version $Version, zips in $(Join-Path $root 'artifacts'):"
$results | Format-Table -AutoSize
