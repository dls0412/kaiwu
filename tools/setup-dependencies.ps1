param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
$temporary = -not $ArchivePath
if ($temporary) { $ArchivePath = Join-Path ([IO.Path]::GetTempPath()) ('kaiwu-dependencies-' + [Guid]::NewGuid().ToString('N') + '.zip') }
try {
    if ($temporary) { Write-Host 'Downloading pinned runtime dependencies...'; Invoke-WebRequest -Uri $manifest.url -OutFile $ArchivePath }
    $hash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $manifest.sha256) { throw 'Dependency archive SHA256 mismatch' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }
            $relative = $entry.FullName.Replace('\','/')
            if ($relative -notmatch '^(tools/engine/|documents/(vendor|helpers|app/engine)/)') { throw "Unexpected dependency path: $relative" }
            $target = [IO.Path]::GetFullPath((Join-Path $root $relative))
            if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid dependency archive path' }
            New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $archive.Dispose() }
    foreach ($file in $manifest.files) {
        $target = Join-Path $root $file.path
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Dependency verification failed: $($file.path)" }
    }
    Write-Host 'Runtime dependencies verified.'
} finally { if ($temporary -and (Test-Path -LiteralPath $ArchivePath)) { [IO.File]::Delete($ArchivePath) } }
