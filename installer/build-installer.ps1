# Publishes the single-file exe and compiles the Inno Setup installer into dist\.
# Needs the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj = Join-Path $root 'src\ObsidianTaskbarIcons\ObsidianTaskbarIcons.csproj'
$dist = Join-Path $root 'dist'
$iss  = Join-Path $root 'installer\ObsidianTaskbarIcons.iss'

[xml]$csproj = Get-Content $proj
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> found in $proj" }

Write-Host "Publishing $version..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$candidates = @(@(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) })
if (-not $candidates) { throw "ISCC.exe not found. Install Inno Setup 6: winget install JRSoftware.InnoSetup" }
$iscc = $candidates[0]

Write-Host "Compiling installer with $iscc..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" /Q $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Join-Path $dist "ObsidianTaskbarIcons-Setup-$version.exe"
Write-Host "Installer: $setup" -ForegroundColor Green
