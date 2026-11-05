param([string]$OutputDirectory = 'dist-v0.4',[switch]$TestBuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Force $output | Out-Null
$vendor = Join-Path $root 'documents\vendor'
$arguments = @('/nologo','/target:winexe','/main:Glint.Program','/platform:x64','/optimize+','/utf8output',"/win32icon:$root\ui\kaiwu.ico","/out:$output\开物.exe",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',"/reference:$vendor\Microsoft.Web.WebView2.Core.dll","/reference:$vendor\Microsoft.Web.WebView2.WinForms.dll","$root\src\Player.cs","$root\src\Captures.cs","$root\src\ImageCanvas.cs","$root\src\Documents.cs","$root\src\documents\DocumentWindow.cs","$root\src\documents\PrintWindow.cs")
if ($TestBuild) { $arguments += '/define:TEST_BUILD' }
$arguments += "$root\src\Conversion.cs"
$arguments += "$root\src\ToolServices.cs","$root\src\Tools.cs",'/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll'
$arguments += "$root\src\LinkDownload.cs"
$arguments += "$root\src\DouyinPage.cs"
$arguments += "$root\src\HomeView.cs","$root\src\WindowChrome.cs","$root\src\SpeechToText.cs"
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
$mediaEngine=Join-Path $root "tools\engine\libmpv-2.dll"
if(-not(Test-Path -LiteralPath $mediaEngine)){$mediaEngine=Join-Path (Split-Path $root -Parent) "libmpv-2.dll"}
Copy-Item -LiteralPath $mediaEngine -Destination "$output\libmpv-2.dll"
Copy-Item -LiteralPath "$vendor\Microsoft.Web.WebView2.Core.dll","$vendor\Microsoft.Web.WebView2.WinForms.dll","$vendor\WebView2Loader.dll" -Destination $output -Force
Copy-Item -LiteralPath "$root\ui\kaiwu.ico" -Destination "$output\kaiwu.ico" -Force
$appSource = Join-Path $root 'documents\app'
$licenseSource = Join-Path $root 'documents\licenses'
if (-not (Test-Path -LiteralPath $appSource)) { $appSource = Join-Path (Split-Path $root -Parent) 'app' }
if (-not (Test-Path -LiteralPath $licenseSource)) { $licenseSource = Join-Path (Split-Path $root -Parent) 'licenses' }
Copy-Item -LiteralPath $appSource -Destination $output -Recurse -Force
Copy-Item -LiteralPath $licenseSource -Destination $output -Recurse -Force
Copy-Item -LiteralPath "$vendor\LICENSE-WebView2.txt" -Destination "$output\licenses" -Force
$helperSource=Join-Path $root 'documents\helpers'
if(-not(Test-Path -LiteralPath $helperSource)){$helperSource=Join-Path (Split-Path $root -Parent) 'helpers'}
if(Test-Path -LiteralPath $helperSource){Copy-Item -LiteralPath $helperSource -Destination $output -Recurse -Force}
Copy-Item -LiteralPath "$root\ui" -Destination $output -Recurse -Force
Write-Host "Built $output\开物.exe"
