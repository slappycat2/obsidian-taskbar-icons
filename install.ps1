# Builds Obsidian Taskbar Icons and installs it to %LOCALAPPDATA%\ObsidianTaskbarIcons\bin.
# Shortcuts created by the tool always target that installed copy, so rebuilding never breaks a pin.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\ObsidianTaskbarIcons\ObsidianTaskbarIcons.csproj'
$dist = Join-Path $root 'dist'
$dest = Join-Path $env:LOCALAPPDATA 'ObsidianTaskbarIcons\bin'

Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$running = Get-Process ObsidianTaskbarIcons -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing running Obsidian Taskbar Icons instance(s) so the exe can be replaced..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $dist '*') $dest -Recurse -Force

# Sample icons: the icon picker opens here by default.
$samples = Join-Path $env:LOCALAPPDATA 'ObsidianTaskbarIcons\samples'
New-Item -ItemType Directory -Force -Path $samples | Out-Null
Copy-Item (Join-Path $root 'samples\icons\*.ico') $samples -Force

$exe = Join-Path $dest 'ObsidianTaskbarIcons.exe'
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Obsidian Taskbar Icons.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dest
$sc.Description = 'Create per-vault taskbar icons for Obsidian'
$sc.Save()

Write-Host "Installed to $exe" -ForegroundColor Green
Write-Host "Start-menu entry: Obsidian Taskbar Icons"

# The watcher owns the window icons of open vaults; killing it above blanked them, so put them back.
if (Get-Process Obsidian -ErrorAction SilentlyContinue) {
    Write-Host "Obsidian is running; re-tagging its windows and starting the icon watcher..." -ForegroundColor Cyan
    & $exe tag
}
