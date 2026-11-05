param([string]$ImagePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
try {
 Add-Type -AssemblyName System.Runtime.WindowsRuntime
 [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
 [Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
 [Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
 $script:asTask=([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'})[0]
 function Await($operation,[Type]$type) { $task=$script:asTask.MakeGenericMethod($type).Invoke($null,@($operation)); $task.Wait(); $task.Result }
 $file=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
 $stream=Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
 $decoder=Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
 $bitmap=Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
 $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
 if(-not $engine){throw 'No installed OCR language is available'}
 $result=Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
 $text=($result.Lines | ForEach-Object {$_.Text}) -join "`r`n"
 $text=[regex]::Replace($text,'(?<=[\u4e00-\u9fff]) +(?=[\u4e00-\u9fff])','')
 [IO.File]::WriteAllText($OutputPath,$text,(New-Object Text.UTF8Encoding($false)))
 $bitmap.Dispose();$stream.Dispose()
} catch { [IO.File]::WriteAllText($OutputPath+'.error',$_.Exception.ToString());exit 1 }
