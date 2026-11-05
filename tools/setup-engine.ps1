$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath "$PSScriptRoot\engine-source.json" -Raw | ConvertFrom-Json
$archive = Join-Path $PSScriptRoot 'mpv.7z'
try {
    Invoke-WebRequest -Uri $source.browser_download_url -OutFile $archive
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLower()
    if ($digest -ne $source.digest) { throw 'Engine checksum mismatch' }
    New-Item -ItemType Directory -Force "$PSScriptRoot\engine" | Out-Null
    & tar -xf $archive -C "$PSScriptRoot\engine"
    if ($LASTEXITCODE -ne 0) { throw 'Engine extraction failed' }
} finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
}
