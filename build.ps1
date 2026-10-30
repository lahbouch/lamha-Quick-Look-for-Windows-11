# Builds dist\Lamha-Setup-<version>.exe. Needs the .NET 10 SDK, Inno Setup 6, and a LibreOffice install to bundle from.
param(
    [string]$LibreOffice = "C:\Program Files\LibreOffice",
    [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [switch]$SkipOffice
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = "$root\dist\app"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH" }

# Everything under these paths is unused by headless conversion: help, UI language packs, the Python runtime,
# gallery art and document templates. Fonts stay: they decide how documents without embedded fonts look.
$officeExcludes = @(
    'help', 'readmes',
    'program\python-core-*', 'program\python.exe', 'program\pythonscript.py', 'program\python*.dll', 'program\wizards', 'program\scripting',
    'share\gallery', 'share\template', 'share\wizards', 'share\basic', 'share\Scripts', 'share\autotext', 'share\autocorr', 'share\wordbook', 'share\extensions', 'share\uno_packages', 'share\numbertext'
)

if (Test-Path "$root\dist\app\libreoffice") {
    Move-Item "$root\dist\app\libreoffice" "$root\dist\libreoffice.keep"
}
if (Test-Path $app) { Remove-Item $app -Recurse -Force }

dotnet publish "$root\src\Lamha\Lamha.csproj" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $app --nologo -v q
if ($LASTEXITCODE) { throw "publish failed" }

if (Test-Path "$root\dist\libreoffice.keep") { Move-Item "$root\dist\libreoffice.keep" "$app\libreoffice" }

if (-not $SkipOffice -or -not (Test-Path "$app\libreoffice")) {
    if (Test-Path "$app\libreoffice") { Remove-Item "$app\libreoffice" -Recurse -Force }
    Copy-Item $LibreOffice "$app\libreoffice" -Recurse
    foreach ($x in $officeExcludes) {
        Get-Item "$app\libreoffice\$x" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    }
    # Interface translations; the bundled copy never shows a window.
    Get-ChildItem "$app\libreoffice\program\resource" -Directory -ErrorAction SilentlyContinue | Where-Object Name -ne 'common' | Remove-Item -Recurse -Force
}

Copy-Item "$root\licenses" "$app\licenses" -Recurse -Force
Copy-Item "$root\docs\USER_GUIDE_AR.md" $app -ErrorAction SilentlyContinue

$bytes = (Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum
"app folder: $([math]::Round($bytes / 1MB)) MB"

& $Iscc /Qp "$root\installer\Lamha.iss"
if ($LASTEXITCODE) { throw "installer failed" }
Get-ChildItem "$root\dist\*.exe" | ForEach-Object { "$($_.Name): $([math]::Round($_.Length / 1MB)) MB" }
