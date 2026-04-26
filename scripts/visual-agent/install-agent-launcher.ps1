param(
  [switch]$WithWindowsStartup
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$LauncherScript = Join-Path $ScriptDir "agent-launcher.ps1"
$PowerShellPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path $PowerShellPath)) {
  $PowerShellPath = (Get-Command powershell.exe -ErrorAction Stop).Source
}

function New-AgentShortcut {
  param(
    [string]$ShortcutPath,
    [string]$Action,
    [string]$Description
  )

  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($ShortcutPath)
  $shortcut.TargetPath = $PowerShellPath
  $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$LauncherScript`" -Action $Action"
  $shortcut.WorkingDirectory = $ProjectRoot
  $shortcut.WindowStyle = 1
  $shortcut.Description = $Description
  $shortcut.IconLocation = "$PowerShellPath,0"
  $shortcut.Save()
}

$desktop = [Environment]::GetFolderPath("Desktop")
$programs = [Environment]::GetFolderPath("Programs")
$startMenuDir = Join-Path $programs "AriadGSM"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

$desktopShortcut = Join-Path $desktop "AriadGSM Agent.lnk"
$menuShortcut = Join-Path $startMenuDir "AriadGSM Agent.lnk"
New-AgentShortcut $desktopShortcut "Gui" "AriadGSM Agent Desktop"
New-AgentShortcut $menuShortcut "Gui" "AriadGSM Agent Desktop"

$created = @($desktopShortcut, $menuShortcut)

if ($WithWindowsStartup) {
  $startup = [Environment]::GetFolderPath("Startup")
  $startupShortcut = Join-Path $startup "AriadGSM Agent - iniciar observador.lnk"
  New-AgentShortcut $startupShortcut "Start" "Inicia el observador AriadGSM al abrir Windows"
  $created += $startupShortcut
}

[pscustomobject]@{
  Installed = $true
  Shortcuts = $created
  StartupEnabled = [bool]$WithWindowsStartup
} | ConvertTo-Json -Depth 4
