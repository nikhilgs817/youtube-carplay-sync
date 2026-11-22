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
$setup = Join-Path $build 'YouTube-MP3-Setup.exe'
$icon = Join-Path $PSScriptRoot 'app.ico'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$toolScript = Join-Path $PSScriptRoot 'setup-tools.ps1'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File |
    Where-Object Name -ne 'Installer.cs' |
    Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /warnaserror+ "/out:$output" `
    "/win32icon:$icon" "/win32manifest:$manifest" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /warnaserror+ "/out:$setup" `
    "/win32icon:$icon" "/win32manifest:$manifest" "/resource:$output,app" "/resource:$toolScript,tools" `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll `
    (Join-Path $PSScriptRoot 'Installer.cs')
if ($LASTEXITCODE -ne 0) { throw "Setup build failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath $toolScript -Destination $app -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $app -Force
$zip = Join-Path $build 'YouTube-MP3-Windows.zip'
Compress-Archive -Path $app -DestinationPath $zip -Force
Write-Host "Created $zip"
Write-Host "Created $setup"
