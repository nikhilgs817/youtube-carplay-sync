$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework compiler was not found.' }

$build = Join-Path $PSScriptRoot 'dist'
$app = Join-Path $build 'YouTube-MP3'
New-Item -ItemType Directory -Path $app -Force | Out-Null
$output = Join-Path $app 'YouTube-MP3.exe'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File |
    Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /warnaserror+ "/out:$output" `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup-tools.ps1') -Destination $app -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $app -Force
$zip = Join-Path $build 'YouTube-MP3-Windows.zip'
Compress-Archive -Path $app -DestinationPath $zip -Force
Write-Host "Created $zip"
