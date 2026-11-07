param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$shared = Join-Path $root 'src\SharedVersion.cs'
$readme = Join-Path $root 'README.md'

if (-not (Test-Path $shared)) {
    Write-Host "[ERROR] Not found: $shared" -ForegroundColor Red
    exit 1
}

function Get-CurrentVersion {
    $text = Get-Content -Raw -Encoding UTF8 $shared
    $m = [regex]::Match($text, 'AssemblyVersion\("(\d+\.\d+\.\d+\.\d+)"\)')
    if ($m.Success) { return $m.Groups[1].Value }
    return 'unknown'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host ""
    Write-Host "VisionSystem version tool" -ForegroundColor Cyan
    Write-Host ("  current : " + (Get-CurrentVersion))
    Write-Host ""
    Write-Host "  usage   : version.bat <major.minor.patch.build>"
    Write-Host "  example : version.bat 1.0.0.1"
    Write-Host ""
    exit 0
}

$Version = $Version.Trim()
if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$') {
    Write-Host "[ERROR] Version must be four numeric parts, e.g. 1.0.0.1" -ForegroundColor Red
    exit 1
}

foreach ($part in $Version.Split('.')) {
    if ([int]$part -gt 65535) {
        Write-Host "[ERROR] Each part must be 0-65535 (AssemblyVersion limit)." -ForegroundColor Red
        exit 1
    }
}

$old = Get-CurrentVersion
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

# ---- 1. src/SharedVersion.cs (single source of truth for all C# projects) ----
$text = Get-Content -Raw -Encoding UTF8 $shared
$text = [regex]::Replace($text, 'AssemblyVersion\("\d+\.\d+\.\d+\.\d+"\)',             ('AssemblyVersion("' + $Version + '")'))
$text = [regex]::Replace($text, 'AssemblyFileVersion\("\d+\.\d+\.\d+\.\d+"\)',         ('AssemblyFileVersion("' + $Version + '")'))
$text = [regex]::Replace($text, 'AssemblyInformationalVersion\("\d+\.\d+\.\d+\.\d+"\)', ('AssemblyInformationalVersion("' + $Version + '")'))
[System.IO.File]::WriteAllText($shared, $text, $utf8Bom)

# ---- 2. README.md version block (ASCII markers, see VERSION_BLOCK_START) ----
if (Test-Path $readme) {
    $md = Get-Content -Raw -Encoding UTF8 $readme
    if ($md -match 'VERSION_BLOCK_START' -and $md -match 'VERSION_BLOCK_END') {
        $pattern = '(?s)(<!-- VERSION_BLOCK_START -->.*?)\d+\.\d+\.\d+\.\d+(.*?<!-- VERSION_BLOCK_END -->)'
        $md = [regex]::Replace($md, $pattern, ('${1}' + $Version + '${2}'))
        [System.IO.File]::WriteAllText($readme, $md, $utf8Bom)
    }
    else {
        Write-Host "[WARN] README has no VERSION_BLOCK markers; skipped." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host ("[OK] version " + $old + " -> " + $Version) -ForegroundColor Green
Write-Host "     updated: src\SharedVersion.cs (all C# projects)"
Write-Host "     updated: README.md"
Write-Host "     next   : build.bat Debug / Release"
Write-Host ""
