param(
  [string]$ConfigPath = "",
  [ValidateSet("wa-1", "wa-2", "wa-3")]
  [string[]]$Channels = @("wa-1", "wa-2", "wa-3"),
  [int]$MaxChatsPerChannel = 2,
  [int]$MaxLinesPerChat = 40,
  [int]$WaitSeconds = 2,
  [switch]$Execute,
  [switch]$Send
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$NavigatorScript = Join-Path $ScriptDir "visual-chat-navigator.ps1"
$CaptureScript = Join-Path $ScriptDir "visual-screen-capture.ps1"
$RuntimeDir = Join-Path $ScriptDir "runtime"
New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

if (-not $ConfigPath) {
  $ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
}

Add-Type -AssemblyName System.Windows.Forms

$mouseApi = @"
using System;
using System.Runtime.InteropServices;
public class VisualLearningMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@
Add-Type -TypeDefinition $mouseApi -ErrorAction SilentlyContinue

function Normalize-LearningText {
  param([string]$Value)
  $text = ([string]$Value).ToLowerInvariant().Normalize([System.Text.NormalizationForm]::FormD)
  return ($text -replace "\p{Mn}", "" -replace "\s+", " ").Trim()
}

function Limit-Text {
  param([string]$Value, [int]$Length = 180)
  $text = (($Value -replace "\s+", " ").Trim())
  if ($text.Length -le $Length) {
    return $text
  }
  return $text.Substring(0, $Length).Trim() + "..."
}

function Test-LearningCandidate {
  param($Candidate)
  $text = (($Candidate.Text -replace "\s+", " ").Trim())
  if ($text.Length -lt 3) { return $false }
  if ($text -notmatch "[\p{L}\p{N}]") { return $false }
  $normalized = Normalize-LearningText $text
  if ($normalized -match "\btu\b") { return $false }

  $blocked = @(
    "^WhatsApp( Business)?$",
    "^Buscar",
    "^Todos$",
    "^Archivados$",
    "^No le.dos$",
    "^Favoritos$",
    "^Grupos$",
    "^Canales$",
    "^Estados$",
    "^Comunidades$",
    "^Llamadas$",
    "^Meta AI$",
    "^Foto$",
    "^Video$",
    "^Audio$",
    "^Sticker$",
    "^Documento$",
    "^Codex$",
    "scripts/visual-agent",
    "visual-chat",
    "AriadGSM Agent",
    "PowerShell",
    "Railway",
    "GitHub",
    "YouTube",
    "Migraci.n",
    "Siguiente pas",
    "pr.ctico",
    "clasificador",
    "capturador",
    "controladores",
    "optimizar",
    "Instalar controladores",
    "\(\s*T[uú]\s*\)",
    "\bT[uú]\b",
    "launcher",
    "^[0-9:\s\.apm]+$"
  )

  foreach ($pattern in $blocked) {
    if ($text -match $pattern) { return $false }
  }

  return $true
}

function Test-BlockedLearningView {
  param($Candidates)
  $text = ((@($Candidates) | ForEach-Object { [string]$_.Text }) -join " ")
  $blockedViewPatterns = @(
    "Codex",
    "scripts/visual-agent",
    "visual-intent",
    "visual-chat",
    "AriadGSM Agent Desktop",
    "PowerShell",
    "Railway",
    "GitHub",
    "YouTube",
    "Siguiente pas",
    "clasificador",
    "capturador",
    "controladores",
    "launcher"
  )

  foreach ($pattern in $blockedViewPatterns) {
    if ($text -match $pattern) {
      return $true
    }
  }

  return $false
}

function Invoke-Click {
  param([int]$X, [int]$Y)
  [void][VisualLearningMouse]::SetCursorPos($X, $Y)
  Start-Sleep -Milliseconds 120
  [VisualLearningMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 120
  [VisualLearningMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-NavigatorList {
  param([string]$Channel)
  if (-not (Test-Path -LiteralPath $NavigatorScript)) {
    throw "No encontre visual-chat-navigator.ps1"
  }

  $output = & $NavigatorScript -Channel $Channel -Action List -MaxResults 30
  $json = ($output | Out-String).Trim()
  if (-not $json) {
    throw "El navegador visual no devolvio lista para $Channel."
  }
  return $json | ConvertFrom-Json
}

function Select-ChatCandidates {
  param($Navigation)
  $seen = @{}
  $selected = @()
  foreach ($candidate in @($Navigation.Candidates | Sort-Object { $_.Click.Y })) {
    if (-not (Test-LearningCandidate $candidate)) { continue }
    $key = Normalize-LearningText $candidate.Text
    if (-not $key -or $seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $selected += $candidate
    if ($selected.Count -ge $MaxChatsPerChannel) { break }
  }
  return $selected
}

function Invoke-ConversationCapture {
  param(
    [string]$Channel,
    [string]$ConversationTitle
  )

  $titleKey = (Normalize-LearningText $ConversationTitle) -replace "[^a-z0-9]+", "-"
  $titleKey = $titleKey.Trim("-")
  if (-not $titleKey) {
    $titleKey = "chat"
  }
  if ($titleKey.Length -gt 48) {
    $titleKey = $titleKey.Substring(0, 48)
  }

  $captureParams = @{
    ConfigPath = $ConfigPath
    ActiveChannel = $Channel
    ActiveConversationTitle = $ConversationTitle
    ActiveConversationId = "learned-chat-$Channel-$titleKey"
    MaxLinesPerChannel = $MaxLinesPerChat
  }
  if ($Send) {
    $captureParams.Send = $true
  }

  $output = @(& $CaptureScript @captureParams)
  $result = $output |
    Where-Object { $_ -and $_.PSObject.Properties["EventFile"] } |
    Select-Object -Last 1
  $logs = @($output |
      Where-Object { -not ($_ -and $_.PSObject.Properties["EventFile"]) } |
      ForEach-Object { [string]$_ })

  return [pscustomobject]@{
    ConversationId = $captureParams.ActiveConversationId
    Result = $result
    Logs = $logs
  }
}

$channelPlans = @()
$opened = @()
$notes = @()

foreach ($channel in $Channels) {
  $navigation = Invoke-NavigatorList -Channel $channel
  if (Test-BlockedLearningView $navigation.Candidates) {
    $channelPlans += [pscustomobject]@{
      Channel = $channel
      Capture = $navigation.Capture
      CandidateCount = @($navigation.Candidates).Count
      SelectedCount = 0
      Selected = @()
    }
    $notes += "Omiti $channel porque parece tener Codex, navegador u otra ventana encima."
    continue
  }

  $candidates = @(Select-ChatCandidates $navigation)
  $channelPlans += [pscustomobject]@{
    Channel = $channel
    Capture = $navigation.Capture
    CandidateCount = @($navigation.Candidates).Count
    SelectedCount = $candidates.Count
    Selected = @($candidates | ForEach-Object {
        [pscustomobject]@{
          Text = Limit-Text ([string]$_.Text)
          Click = $_.Click
        }
      })
  }

  if (-not $candidates.Count) {
    $notes += "No encontre filas confiables para aprender en $channel."
    continue
  }

  if (-not $Execute) {
    continue
  }

  foreach ($candidate in $candidates) {
    Invoke-Click -X ([int]$candidate.Click.X) -Y ([int]$candidate.Click.Y)
    Start-Sleep -Seconds ([Math]::Max(0, $WaitSeconds))
    $capture = Invoke-ConversationCapture -Channel $channel -ConversationTitle ([string]$candidate.Text)
    $opened += [pscustomobject]@{
      Channel = $channel
      Title = Limit-Text ([string]$candidate.Text)
      Click = $candidate.Click
      Capture = $capture
    }
  }
}

$status = if ($Execute -and $opened.Count) {
  "executed"
} elseif ($Execute) {
  "executed_no_chats"
} elseif (($channelPlans | Measure-Object -Property SelectedCount -Sum).Sum -gt 0) {
  "preview_ready"
} else {
  "preview_no_chats"
}

[pscustomobject]@{
  Status = $status
  Execute = [bool]$Execute
  Send = [bool]$Send
  Channels = $Channels
  MaxChatsPerChannel = $MaxChatsPerChannel
  MaxLinesPerChat = $MaxLinesPerChat
  Plans = $channelPlans
  Opened = $opened
  Notes = $notes
} | ConvertTo-Json -Depth 10
