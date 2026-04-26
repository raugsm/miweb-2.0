param(
  [string]$ConfigPath = "",
  [switch]$Watch,
  [int]$PollSeconds = 30,
  [int]$MaxCycles = 1,
  [int]$LearningEveryCycles = 6,
  [int]$MaxChatsPerChannel = 1,
  [int]$MaxLinesPerChat = 25,
  [int]$MaxLinesPerCapture = 20,
  [int]$IntentMaxQueries = 3,
  [double]$IntentWaitSeconds = 0.8,
  [double]$LearningWaitSeconds = 0.8,
  [switch]$LearnOnFirstCycle,
  [switch]$Execute,
  [switch]$Send,
  [switch]$OpenWhatsApp,
  [switch]$ArrangeWindows
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$CaptureScript = Join-Path $ScriptDir "visual-screen-capture.ps1"
$IntentBridgeScript = Join-Path $ScriptDir "visual-intent-bridge.ps1"
$LearningPassScript = Join-Path $ScriptDir "visual-chat-learning-pass.ps1"
$RuntimeDir = Join-Path $ScriptDir "runtime"
$AutopilotStateFile = Join-Path $RuntimeDir "agent-autopilot.state.json"
New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

if (-not $ConfigPath) {
  $ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
}

Add-Type -AssemblyName System.Windows.Forms

$windowApi = @"
using System;
using System.Runtime.InteropServices;
public class VisualAutopilotWindow {
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
Add-Type -TypeDefinition $windowApi -ErrorAction SilentlyContinue

function Read-AgentConfig {
  if (Test-Path -LiteralPath $ConfigPath) {
    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
  }

  return [pscustomobject]@{
    cloudUrl = "https://ariadgsm.com"
    agentToken = ""
  }
}

function Write-AutopilotLine {
  param([string]$Message)
  $stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
  Write-Host "[$stamp] $Message"
}

function Invoke-JsonScript {
  param(
    [string]$Path,
    [hashtable]$Parameters
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "No encontre $Path"
  }

  $output = @(& $Path @Parameters)
  $json = ($output | Out-String).Trim()
  if (-not $json) {
    throw "$Path no devolvio JSON."
  }
  return $json | ConvertFrom-Json
}

function Invoke-CaptureStep {
  param([string]$Name)
  $params = @{
    ConfigPath = $ConfigPath
    MaxLinesPerChannel = $MaxLinesPerCapture
  }
  if ($Send) {
    $params.Send = $true
  }

  $output = @(& $CaptureScript @params)
  $result = $output |
    Where-Object { $_ -and $_.PSObject.Properties["EventFile"] } |
    Select-Object -Last 1

  return [pscustomobject]@{
    Step = $Name
    Sent = [bool]$Send
    Result = $result
    RawCount = $output.Count
  }
}

function Invoke-IntentStep {
  $params = @{
    ConfigPath = $ConfigPath
    MaxLinesPerChannel = $MaxLinesPerCapture
    MaxQueries = $IntentMaxQueries
    WaitSeconds = $IntentWaitSeconds
  }
  if ($Execute) {
    $params.Execute = $true
    $params.CaptureAfterOpen = $true
  }
  if ($Send -and $Execute) {
    $params.Send = $true
  }

  return Invoke-JsonScript -Path $IntentBridgeScript -Parameters $params
}

function Invoke-LearningStep {
  $params = @{
    ConfigPath = $ConfigPath
    MaxChatsPerChannel = $MaxChatsPerChannel
    MaxLinesPerChat = $MaxLinesPerChat
    WaitSeconds = $LearningWaitSeconds
  }
  if ($Execute) {
    $params.Execute = $true
  }
  if ($Send -and $Execute) {
    $params.Send = $true
  }

  return Invoke-JsonScript -Path $LearningPassScript -Parameters $params
}

function Get-LaunchTargets {
  param($Config)
  $targets = @()
  if ($Config -and $Config.autopilot -and $Config.autopilot.launchTargets) {
    foreach ($target in @($Config.autopilot.launchTargets)) {
      if ($target.command) {
        $targets += [pscustomobject]@{
          Name = if ($target.name) { $target.name } else { "WhatsApp" }
          Command = [string]$target.command
        }
      }
    }
  }

  if (-not $targets.Count) {
    $targets = @(
      [pscustomobject]@{ Name = "WhatsApp"; Command = "whatsapp:" }
    )
  }

  return $targets
}

function Get-WhatsAppWindows {
  $windows = @(Get-Process -ErrorAction SilentlyContinue |
      Where-Object {
        $_.MainWindowHandle -ne [IntPtr]::Zero -and
        $_.MainWindowTitle -match "WhatsApp"
      } |
      ForEach-Object {
        $priority = switch -Regex ($_.ProcessName) {
          "^WhatsApp" { 0; break }
          "^msedgewebview2$" { 1; break }
          "^chrome$|^msedge$" { 2; break }
          default { 3 }
        }
        [pscustomobject]@{
          Process = $_
          ProcessName = $_.ProcessName
          ProcessId = $_.Id
          Title = $_.MainWindowTitle
          Handle = $_.MainWindowHandle
          Priority = $priority
        }
      } |
      Sort-Object Priority, ProcessName, ProcessId)

  return $windows
}

function Open-WhatsAppTargets {
  param($Config)
  $opened = @()
  $existingWindows = @(Get-WhatsAppWindows)
  if ($existingWindows.Count) {
    return @(
      [pscustomobject]@{
        Name = "WhatsApp visible"
        Command = "skip"
        Opened = $false
        Error = $null
        Skipped = $true
        Reason = "Ya hay $($existingWindows.Count) ventana(s) visible(s) de WhatsApp."
      }
    )
  }

  foreach ($target in @(Get-LaunchTargets $Config)) {
    try {
      Start-Process $target.Command
      $opened += [pscustomobject]@{
        Name = $target.Name
        Command = $target.Command
        Opened = $true
        Error = $null
      }
    } catch {
      $opened += [pscustomobject]@{
        Name = $target.Name
        Command = $target.Command
        Opened = $false
        Error = $_.Exception.Message
      }
    }
  }
  return $opened
}

function Arrange-WhatsAppWindows {
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  $windows = @(Get-WhatsAppWindows | Select-Object -First 3)

  $arranged = @()
  if ($windows.Count -lt 3) {
    return $arranged
  }

  $regionWidth = [Math]::Floor($screen.Width / 3)
  for ($i = 0; $i -lt $windows.Count; $i++) {
    $window = $windows[$i]
    $process = $window.Process
    $left = $screen.Left + ($regionWidth * $i)
    $width = if ($i -eq 2) { $screen.Right - $left } else { $regionWidth }
    [void][VisualAutopilotWindow]::ShowWindow($process.MainWindowHandle, 9)
    [void][VisualAutopilotWindow]::MoveWindow($process.MainWindowHandle, $left, $screen.Top, $width, $screen.Height, $true)
    $arranged += [pscustomobject]@{
      ProcessId = $process.Id
      Title = $process.MainWindowTitle
      Left = $left
      Top = $screen.Top
      Width = $width
      Height = $screen.Height
    }
  }

  return $arranged
}

function Invoke-AutopilotCycle {
  param([int]$CycleNumber, $Config)

  $notes = @()
  $openedTargets = @()
  $arrangedWindows = @()

  if ($CycleNumber -eq 1 -and $OpenWhatsApp) {
    Write-AutopilotLine "Abriendo destinos de WhatsApp configurados."
    $openedTargets = @(Open-WhatsAppTargets $Config)
    if (@($openedTargets | Where-Object { $_.Opened }).Count) {
      Start-Sleep -Seconds 4
    }
  }

  if ($ArrangeWindows) {
    Write-AutopilotLine "Acomodando ventanas de WhatsApp visibles."
    $arrangedWindows = @(Arrange-WhatsAppWindows)
    if (-not $arrangedWindows.Count) {
      $notes += "No encontre ventanas con titulo WhatsApp para acomodar."
    }
    Start-Sleep -Milliseconds 500
  }

  Write-AutopilotLine "Ciclo ${CycleNumber}: captura base."
  $baseCapture = Invoke-CaptureStep -Name "base_capture"

  Write-AutopilotLine "Ciclo ${CycleNumber}: atendiendo alertas clasificadas."
  $intent = $null
  try {
    $intent = Invoke-IntentStep
  } catch {
    $notes += "Intent bridge fallo: $($_.Exception.Message)"
  }

  $learning = $null
  if (($LearnOnFirstCycle -and $CycleNumber -eq 1) -or ($LearningEveryCycles -gt 0 -and (($CycleNumber % $LearningEveryCycles) -eq 0))) {
    Write-AutopilotLine "Ciclo ${CycleNumber}: aprendizaje de chats visibles."
    try {
      $learning = Invoke-LearningStep
    } catch {
      $notes += "Learning pass fallo: $($_.Exception.Message)"
    }
  }

  $status = "ok"
  if ($notes.Count) {
    $status = "ok_with_notes"
  }

  return [pscustomobject]@{
    Status = $status
    Cycle = $CycleNumber
    Execute = [bool]$Execute
    Send = [bool]$Send
    OpenWhatsApp = [bool]$OpenWhatsApp
    ArrangeWindows = [bool]$ArrangeWindows
    OpenedTargets = $openedTargets
    ArrangedWindows = $arrangedWindows
    BaseCapture = $baseCapture
    Intent = $intent
    Learning = $learning
    Notes = $notes
    FinishedAt = (Get-Date).ToUniversalTime().ToString("o")
  }
}

$config = Read-AgentConfig
$cycle = 0
$summaries = @()

do {
  $cycle += 1
  $summary = Invoke-AutopilotCycle -CycleNumber $cycle -Config $config
  $summaries += $summary
  $summaryJson = $summary | ConvertTo-Json -Depth 12
  $summaryJson | Set-Content -LiteralPath $AutopilotStateFile -Encoding UTF8
  $summaryJson

  $shouldContinue = $Watch -and ($MaxCycles -le 0 -or $cycle -lt $MaxCycles)
  if ($shouldContinue) {
    Write-AutopilotLine "Esperando $PollSeconds segundo(s) para el siguiente ciclo."
    Start-Sleep -Seconds ([Math]::Max(10, $PollSeconds))
  }
} while ($shouldContinue)

if (-not $Watch) {
  return
}
