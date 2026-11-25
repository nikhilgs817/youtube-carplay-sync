$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework compiler was not found.' }

$build = Join-Path $PSScriptRoot 'dist'
$app = Join-Path $build 'YouTube-CarPlay-Sync'
New-Item -ItemType Directory -Path $app -Force | Out-Null
$output = Join-Path $app 'YouTube-CarPlay-Sync.exe'
$setup = Join-Path $build 'YouTube-CarPlay-Sync-Setup.exe'
$icon = Join-Path $PSScriptRoot 'app.ico'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$common = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/warn:4',
    "/win32icon:$icon", "/win32manifest:$manifest",
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Net.Http.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.Security.dll', "/resource:$icon,appicon")

# The app: every source file except the installer.
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File |
    Where-Object Name -ne 'Installer.cs' |
    Select-Object -ExpandProperty FullName
& $compiler @common "/out:$output" /reference:System.Web.Extensions.dll /reference:System.Xml.Linq.dll $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

# The installer carries the app, shares the look (Ui.cs) and downloads the audio tools itself (Tools.cs).
$setupSources = 'Installer.cs', 'AppInfo.cs', 'Tools.cs', 'Ui.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler @common "/out:$setup" "/resource:$output,app" /reference:Microsoft.CSharp.dll $setupSources
if ($LASTEXITCODE -ne 0) { throw "Setup build failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $app -Force
$zip = Join-Path $build 'YouTube-CarPlay-Sync-Windows.zip'
Compress-Archive -Path $app -DestinationPath $zip -Force
Write-Host "Created $zip"
Write-Host "Created $setup"
