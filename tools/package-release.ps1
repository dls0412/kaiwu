param([string]$Version = '0.4.0', [string]$Owner = 'dls0412', [string]$Repository = 'kaiwu', [switch]$ReuseDependencies)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root '.publish'
New-Item -ItemType Directory -Force $output | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-Archive([string]$Path, $Entries) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($entry in $Entries) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.source,$entry.name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
    finally { $zip.Dispose(); $stream.Dispose() }
}
if (-not $ReuseDependencies) {
$dependencies = @()
$dependencies += Get-Item -LiteralPath (Join-Path $root 'tools/engine/libmpv-2.dll')
$dependencies += Get-ChildItem -LiteralPath (Join-Path $root 'documents/vendor') -File -Filter '*.dll'
$dependencies += Get-ChildItem -LiteralPath (Join-Path $root 'documents/helpers') -Recurse -File
$dependencies += Get-ChildItem -LiteralPath (Join-Path $root 'documents/app/engine') -File -Filter '*.br'
$files = @($dependencies | ForEach-Object { @{ path=$_.FullName.Substring($root.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes=$_.Length } })
$dependencyName = "kaiwu-dependencies-v$Version.zip"
Write-Archive (Join-Path $output $dependencyName) @($dependencies | ForEach-Object { @{source=$_.FullName; name=$_.FullName.Substring($root.Length+1).Replace('\','/')} })
$manifest = @{version=$Version;url="https://github.com/$Owner/$Repository/releases/download/v$Version/$dependencyName";sha256=(Get-FileHash -LiteralPath (Join-Path $output $dependencyName) -Algorithm SHA256).Hash.ToLowerInvariant();files=$files}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Encoding UTF8
}
$runtime = Join-Path $root 'dist-v0.4'
$entries = @()
foreach ($name in @('开物.exe','libmpv-2.dll','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','kaiwu.ico')) {
    $entries += @{source=(Join-Path $runtime $name);name="kaiwu-v$Version/$name"}
}
foreach ($folder in @('app','helpers','ui')) {
    $entries += Get-ChildItem -LiteralPath (Join-Path $runtime $folder) -Recurse -File | ForEach-Object { @{source=$_.FullName;name="kaiwu-v$Version/"+$_.FullName.Substring($runtime.Length+1).Replace('\','/')} }
}
$entries += Get-ChildItem -LiteralPath (Join-Path $root 'documents/licenses') -File | ForEach-Object { @{source=$_.FullName;name="kaiwu-v$Version/licenses/"+$_.Name} }
$entries += @{source=(Join-Path $root 'documents/vendor/LICENSE-WebView2.txt');name="kaiwu-v$Version/licenses/LICENSE-WebView2.txt"}
foreach ($name in @('README.md','LICENSE.txt','THIRD-PARTY.md')) { $entries += @{source=(Join-Path $root $name);name="kaiwu-v$Version/$name"} }
Write-Archive (Join-Path $output "kaiwu-v$Version-windows-x64.zip") $entries
$checksumNames = @("kaiwu-v$Version-windows-x64.zip")
if (-not $ReuseDependencies) { $checksumNames += $dependencyName }
$checksums = $checksumNames | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $output $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ }
$checksums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
Write-Host 'Release and dependency archives are ready.'
