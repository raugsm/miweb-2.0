param(
  [string]$ConfigPath = "",
  [ValidateSet("wa-1", "wa-2", "wa-3")]
  [string[]]$Channels = @("wa-1", "wa-2", "wa-3"),
  [int]$MaxChatsPerChannel = 2,
  [int]$MaxLinesPerChat = 40,
  [int]$MaxScrollPages = 4,
  [int]$ScrollWheelClicks = 5,
  [int]$HistoryMonths = 1,
  [double]$WaitSeconds = 0.8,
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

$DefaultSkipLearningChatPatterns = @(
  "^Pagos?\s+(Mexico|M\.xico|M.xico|Chile|Colombia|Peru|Per.)\b.*$",
  "^Pagos?\s+(MX|CL|CO|PE)\b.*$",
  "\bpagos?\b.*\b(mexico|chile|colombia|peru|mx|cl|co|pe)\b"
)

function Read-LearningConfig {
  if (Test-Path -LiteralPath $ConfigPath) {
    try {
      return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    } catch {
      return $null
    }
  }
  return $null
}

$learningConfig = Read-LearningConfig
$script:SkipLearningChatPatterns = @($DefaultSkipLearningChatPatterns)
foreach ($pattern in @($learningConfig.autopilot.skipLearningChats)) {
  if ($pattern) {
    $script:SkipLearningChatPatterns += [string]$pattern
  }
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

function Test-LearningTimeOnlyLine {
  param([string]$Text)
  $value = ($Text -replace "\s+", " ").Trim()
  $hasClock = $value -match "\b[0-9gqloOiIl]{1,2}\s*[:;]\s*[0-9oO]{2}"
  $hasAmpm = $value -match "\b[apu]\.?\s*(m|rn|nm|urn|um|r)\.?\b"
  if (-not ($hasClock -or $hasAmpm)) {
    return $false
  }

  $semantic = $value.ToLowerInvariant()
  $semantic = $semantic -replace "\b[0-9gqloOiIl]{1,2}\s*[:;]\s*[0-9oO]{2}", ""
  $semantic = $semantic -replace "\b[apu]\.?\s*(m|rn|nm|urn|um|r)\.?\b", ""
  $semantic = $semantic -replace "[^a-z0-9\p{L}]+", ""
  return $semantic.Length -le 4
}

function Test-SkipLearningText {
  param([string]$Value)
  $text = (($Value -replace "\s+", " ").Trim())
  $normalized = Normalize-LearningText $text

  if ($normalized -match "\bpagos?\b.*\b(mexico|chile|colombia|peru|mx|cl|co|pe)\b") {
    return $true
  }

  foreach ($pattern in @($script:SkipLearningChatPatterns)) {
    if (-not $pattern) { continue }
    if ($text -match $pattern -or $normalized -match $pattern) {
      return $true
    }
  }

  return $false
}

function Test-LearningCandidate {
  param($Candidate)
  $text = (($Candidate.Text -replace "\s+", " ").Trim())
  if ($text.Length -lt 3) { return $false }
  if ($text -notmatch "[\p{L}\p{N}]") { return $false }
  if (Test-LearningTimeOnlyLine $text) { return $false }
  $normalized = Normalize-LearningText $text
  if ($normalized -match "\btu\b") { return $false }

  $blocked = @(
    "^WhatsApp( Business)?$",
    "^WhatsApp\s+\d+:?$",
    "^ARIADGSM\s+WhatsApp\b",
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

  if (Test-SkipLearningText $text) {
    return $false
  }

  return $true
}

function Get-LearningRowCandidates {
  param($Navigation, $Candidate)
  $candidateY = [int]$Candidate.Click.Y
  return @($Navigation.Candidates |
    Where-Object {
      $_.Click -and [Math]::Abs(([int]$_.Click.Y) - $candidateY) -le 36
    } |
    Sort-Object { $_.Rect.Left })
}

function Get-LearningRowText {
  param($Navigation, $Candidate)
  $row = @(Get-LearningRowCandidates -Navigation $Navigation -Candidate $Candidate)
  return (($row | ForEach-Object { [string]$_.Text }) -join " " -replace "\s+", " ").Trim()
}

function Test-LearningCandidateRow {
  param($Navigation, $Candidate)
  $rowText = Get-LearningRowText -Navigation $Navigation -Candidate $Candidate
  if (Test-SkipLearningText $rowText) {
    return $false
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

function Wait-LearningDelay {
  $milliseconds = [Math]::Max(100, [int]($WaitSeconds * 1000))
  Start-Sleep -Milliseconds $milliseconds
}

function Get-ChannelBounds {
  param([string]$Channel)
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $index = switch ($Channel) {
    "wa-1" { 0 }
    "wa-2" { 1 }
    "wa-3" { 2 }
  }
  $regionWidth = [Math]::Floor($screen.Width / 3)
  $left = $screen.Left + ($regionWidth * $index)
  $right = if ($index -eq 2) { $screen.Right } else { $left + $regionWidth }

  return [pscustomobject]@{
    Left = $left
    Top = $screen.Top
    Width = $right - $left
    Height = $screen.Height
  }
}

function Get-ConversationScrollPoint {
  param([string]$Channel)
  $bounds = Get-ChannelBounds $Channel
  return [pscustomobject]@{
    X = [Math]::Floor($bounds.Left + ($bounds.Width * 0.72))
    Y = [Math]::Floor($bounds.Top + ($bounds.Height * 0.55))
  }
}

function Invoke-ScrollUpInConversation {
  param([string]$Channel)
  $point = Get-ConversationScrollPoint $Channel
  [void][VisualLearningMouse]::SetCursorPos([int]$point.X, [int]$point.Y)
  Start-Sleep -Milliseconds 80
  $wheelData = [int](120 * [Math]::Max(1, $ScrollWheelClicks))
  [VisualLearningMouse]::mouse_event(0x0800, 0, 0, $wheelData, [UIntPtr]::Zero)
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
    if (-not (Test-LearningCandidateRow -Navigation $Navigation -Candidate $candidate)) { continue }
    if (@($selected | Where-Object { [Math]::Abs(([int]$_.Click.Y) - ([int]$candidate.Click.Y)) -le 36 }).Count) { continue }
    $key = Normalize-LearningText $candidate.Text
    if (-not $key -or $seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $selected += $candidate
    if ($selected.Count -ge $MaxChatsPerChannel) { break }
  }
  return $selected
}

function Invoke-ScreenConversationCapture {
  param(
    [string]$Channel,
    [string]$ConversationTitle,
    [string]$ConversationId,
    [switch]$SendCapture
  )

  $captureParams = @{
    ConfigPath = $ConfigPath
    ActiveChannel = $Channel
    ActiveConversationTitle = $ConversationTitle
    ActiveConversationId = $ConversationId
    MaxLinesPerChannel = $MaxLinesPerChat
  }
  if ($SendCapture) {
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
    Sent = [bool]$SendCapture
    Result = $result
    Logs = $logs
  }
}

function Get-LearningConversationId {
  param([string]$Channel, [string]$ConversationTitle)
  $titleKey = (Normalize-LearningText $ConversationTitle) -replace "[^a-z0-9]+", "-"
  $titleKey = $titleKey.Trim("-")
  if (-not $titleKey) {
    $titleKey = "chat"
  }
  if ($titleKey.Length -gt 48) {
    $titleKey = $titleKey.Substring(0, 48)
  }

  return "learned-chat-$Channel-$titleKey"
}

function Get-CaptureHistoryDecision {
  param(
    $CaptureResult,
    [string]$Channel
  )

  $months = [Math]::Max(1, $HistoryMonths)
  $cutoff = (Get-Date).Date.AddMonths(-1 * $months)
  $region = @($CaptureResult.Regions) | Where-Object { $_.ChannelId -eq $Channel } | Select-Object -First 1
  $markers = @()

  foreach ($marker in @($region.DateMarkers)) {
    if (-not $marker.Date) { continue }
    $parsedDate = [datetime]::MinValue
    if ([datetime]::TryParseExact([string]$marker.Date, "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$parsedDate)) {
      $markers += [pscustomobject]@{
        Text = [string]$marker.Text
        Date = $parsedDate.Date
        Kind = [string]$marker.Kind
      }
    }
  }

  if (-not $markers.Count) {
    return [pscustomobject]@{
      CutoffDate = $cutoff.ToString("yyyy-MM-dd")
      HistoryMonths = $months
      HasDateMarkers = $false
      OldestDate = $null
      NewestDate = $null
      SkipSend = $false
      StopAfterPage = $false
      Reason = "no_date_markers"
      DateMarkers = @()
    }
  }

  $ordered = @($markers | Sort-Object Date)
  $oldMarkers = @($markers | Where-Object { $_.Date -lt $cutoff })
  $newMarkers = @($markers | Where-Object { $_.Date -ge $cutoff })
  $skipSend = $oldMarkers.Count -gt 0 -and $newMarkers.Count -eq 0
  $stopAfterPage = $oldMarkers.Count -gt 0
  $reason = if ($skipSend) {
    "page_before_history_limit"
  } elseif ($stopAfterPage) {
    "history_limit_boundary_reached"
  } else {
    "within_history_limit"
  }

  return [pscustomobject]@{
    CutoffDate = $cutoff.ToString("yyyy-MM-dd")
    HistoryMonths = $months
    HasDateMarkers = $true
    OldestDate = $ordered[0].Date.ToString("yyyy-MM-dd")
    NewestDate = $ordered[-1].Date.ToString("yyyy-MM-dd")
    SkipSend = $skipSend
    StopAfterPage = $stopAfterPage
    Reason = $reason
    DateMarkers = @($markers | ForEach-Object {
        [pscustomobject]@{
          Text = $_.Text
          Date = $_.Date.ToString("yyyy-MM-dd")
          Kind = $_.Kind
        }
      })
  }
}

function Invoke-ConversationCapture {
  param(
    [string]$Channel,
    [string]$ConversationTitle,
    [string]$ConversationId
  )

  $probe = Invoke-ScreenConversationCapture -Channel $Channel -ConversationTitle $ConversationTitle -ConversationId $ConversationId
  $history = Get-CaptureHistoryDecision -CaptureResult $probe.Result -Channel $Channel
  $sentRun = $null

  if ($Send -and -not $history.SkipSend) {
    $sentRun = Invoke-ScreenConversationCapture -Channel $Channel -ConversationTitle $ConversationTitle -ConversationId $ConversationId -SendCapture
  }

  return [pscustomobject]@{
    ConversationId = $ConversationId
    Sent = [bool]$sentRun
    Result = if ($sentRun) { $sentRun.Result } else { $probe.Result }
    Probe = $probe
    SentRun = $sentRun
    History = $history
    Logs = @($probe.Logs) + $(if ($sentRun) { @($sentRun.Logs) } else { @() })
  }
}

function Invoke-ConversationLearningSweep {
  param(
    [string]$Channel,
    [string]$ConversationTitle
  )

  $conversationId = Get-LearningConversationId -Channel $Channel -ConversationTitle $ConversationTitle
  $pages = @()
  $maxScrolls = [Math]::Max(0, $MaxScrollPages)

  for ($pageIndex = 0; $pageIndex -le $maxScrolls; $pageIndex++) {
    $capture = Invoke-ConversationCapture -Channel $Channel -ConversationTitle $ConversationTitle -ConversationId $conversationId
    $pages += [pscustomobject]@{
      Page = $pageIndex
      Direction = if ($pageIndex -eq 0) { "current" } else { "older" }
      Capture = $capture
    }

    if ($capture.History.SkipSend -or $capture.History.StopAfterPage) {
      break
    }

    if ($pageIndex -lt $maxScrolls) {
      Invoke-ScrollUpInConversation -Channel $Channel
      Wait-LearningDelay
    }
  }

  return [pscustomobject]@{
    ConversationId = $conversationId
    PageCount = $pages.Count
    MaxScrollPages = $MaxScrollPages
    HistoryMonths = $HistoryMonths
    Pages = $pages
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
    Wait-LearningDelay
    $capture = Invoke-ConversationLearningSweep -Channel $channel -ConversationTitle ([string]$candidate.Text)
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
  MaxScrollPages = $MaxScrollPages
  ScrollWheelClicks = $ScrollWheelClicks
  HistoryMonths = $HistoryMonths
  Plans = $channelPlans
  Opened = $opened
  Notes = $notes
} | ConvertTo-Json -Depth 14
