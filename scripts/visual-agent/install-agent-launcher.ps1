param(
  [switch]$WithWindowsStartup
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$LauncherScript = Join-Path $ProjectRoot "AriadGSM-Agent.vbs"
$WScriptPath = Join-Path $env:WINDIR "System32\wscript.exe"
if (-not (Test-Path $WScriptPath)) {
  $WScriptPath = (Get-Command wscript.exe -ErrorAction Stop).Source
}

function New-AgentShortcut {
  param(
    [string]$ShortcutPath,
    [string]$Action,
    [string]$Description
  )

  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($ShortcutPath)
  $shortcut.TargetPath = $WScriptPath
  $shortcut.Arguments = "`"$LauncherScript`" -Action $Action"
  $shortcut.WorkingDirectory = $ProjectRoot
  $shortcut.WindowStyle = 1
  $shortcut.Description = $Description
  $shortcut.IconLocation = "$WScriptPath,0"
  $shortcut.Save()
  Set-ShortcutRunAsAdmin $ShortcutPath
}

function Set-ShortcutRunAsAdmin {
  param([string]$ShortcutPath)
  if (-not (Test-Path -LiteralPath $ShortcutPath)) {
    return
  }

  $bytes = [System.IO.File]::ReadAllBytes($ShortcutPath)
  if ($bytes.Length -le 0x15) {
    return
  }

  $bytes[0x15] = $bytes[0x15] -bor 0x20
  [System.IO.File]::WriteAllBytes($ShortcutPath, $bytes)
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
