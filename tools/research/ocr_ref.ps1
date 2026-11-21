# 用 Windows 内置 OCR (WinRT) 读取「流程图参考图.png」中的文字，
# 输出为 tools/ocr_ref.txt：每行格式  文本 <TAB> x,y,w,h
param(
    [string]$Img = "C:\Users\admin\Desktop\VisionSystem\流程图参考图.png",
    [string]$Out = "C:\Users\admin\Desktop\VisionSystem\tools\ocr_ref.txt"
)

$ErrorActionPreference = "Stop"
$log = "C:\Users\admin\Desktop\VisionSystem\tools\ocr_log.txt"
function Write-Log($m) { Add-Content -Path $log -Value $m -Encoding UTF8 }

try {
    [System.IO.File]::WriteAllText($log, "start $(Get-Date -Format o)`r`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Log "begin"

    Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null

    $asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    })[0]

    function Await($WinRtTask, $ResultType) {
        $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
        $netTask = $asTask.Invoke($null, @($WinRtTask))
        try {
            $netTask.Wait(-1) | Out-Null
        } catch {
            $msg = $_.Exception.Message
            Write-Log "Wait threw: $msg"
            if ($netTask.Exception) {
                foreach ($ie in $netTask.Exception.InnerExceptions) {
                    Write-Log ("  inner: [" + $ie.GetType().FullName + "] " + $ie.Message + " (HResult=0x" + $ie.HResult.ToString("X8") + ")")
                }
            } elseif ($_.Exception.InnerException) {
                Write-Log ("  inner2: " + $_.Exception.InnerException.Message)
            }
            throw
        }
        return $netTask.Result
    }

    [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
    [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
    [Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType = WindowsRuntime] | Out-Null

    Write-Log "types ok"

    $langs = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
    Write-Log ("langs: " + (($langs | ForEach-Object { $_.LanguageTag }) -join ', '))

    $lang = $langs | Where-Object { $_.LanguageTag -eq 'zh-Hans-CN' } | Select-Object -First 1
    if (-not $lang) { $lang = $langs | Where-Object { $_.LanguageTag -like 'zh*' } | Select-Object -First 1 }
    if (-not $lang) { $lang = $langs | Where-Object { $_.LanguageTag -like 'en*' } | Select-Object -First 1 }
    if (-not $lang) { throw "no OCR language pack" }
    Write-Log ("use lang: " + $lang.LanguageTag)

    # 不用 StorageFile（WinRT 代理在此环境下拿不到文件），改从 .NET 流转换
    [System.IO.WindowsRuntimeStreamExtensions, System.IO, ContentType = WindowsRuntime] | Out-Null
    $fs = [System.IO.File]::OpenRead($Img)
    $stream = [System.IO.WindowsRuntimeStreamExtensions]::AsRandomAccessStream($fs)
    $dec    = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bmp    = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $fs.Dispose()
    Write-Log "bitmap loaded"

    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang)
    if (-not $engine) { throw "cannot create engine" }
    $res = Await ($engine.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])
    Write-Log ("lines: " + $res.Lines.Count)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# OCR result  lang=" + $lang.LanguageTag + "  lines=" + $res.Lines.Count)
    [void]$sb.AppendLine("# format: text <TAB> x,y,w,h")
    foreach ($ln in $res.Lines) {
        $b = $ln.BoundingRect
        [void]$sb.AppendLine($ln.Text + "`t" + [int]$b.X + "," + [int]$b.Y + "," + [int]$b.Width + "," + [int]$b.Height)
    }
    [System.IO.File]::WriteAllText($Out, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Log "written to $Out"
}
catch {
    Write-Log ("ERROR: " + $_.Exception.Message)
    Write-Log $_.ScriptStackTrace
}
