param(
  [string]$ConfigPath = "",
  [ValidateSet("Live", "Full")]
  [string]$Mode = "Live",
  [switch]$Watch,
  [int]$PollSeconds = 30,
  [int]$LiveMinPollSeconds = 3,
  [int]$MaxCycles = 1,
  [int]$LearningEveryCycles = 6,
  [int]$MaxChatsPerChannel = 1,
  [int]$MaxLinesPerChat = 25,
  [int]$MaxScrollPages = 2,
  [int]$ScrollWheelClicks = 5,
  [int]$HistoryMonths = 1,
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
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$CaptureScript = Join-Path $ScriptDir "visual-screen-capture.ps1"
$IntentBridgeScript = Join-Path $ScriptDir "visual-intent-bridge.ps1"
$LearningPassScript = Join-Path $ScriptDir "visual-chat-learning-pass.ps1"
$LocalDecisionScript = Join-Path $ScriptDir "visual-local-decision.js"
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

function Resolve-AgentPath {
  param([string]$Value, [string]$Fallback)
  $target = if ($Value) { $Value } else { $Fallback }
  if ([System.IO.Path]::IsPathRooted($target)) {
    return $target
  }
  return Join-Path $ScriptDir $target
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
  $sendNow = $Send -and $Mode -ne "Live"
  $params = @{
    ConfigPath = $ConfigPath
    MaxLinesPerChannel = $MaxLinesPerCapture
  }
  if ($sendNow) {
    $params.Send = $true
  }

  $output = @(& $CaptureScript @params)
  $result = $output |
    Where-Object { $_ -and $_.PSObject.Properties["EventFile"] } |
    Select-Object -Last 1

  return [pscustomobject]@{
    Step = $Name
    Sent = [bool]$sendNow
    Result = $result
    RawCount = $output.Count
  }
}

function Publish-CaptureStep {
  param($CaptureResult, $Config)
  if (-not $Send -or $Mode -ne "Live") {
    return $null
  }
  if (-not $CaptureResult -or -not $CaptureResult.EventFile -or -not (Test-Path -LiteralPath $CaptureResult.EventFile)) {
    return [pscustomobject]@{
      Published = $false
      Reason = "No encontre el JSON local para publicar."
    }
  }

  $targetDir = Resolve-AgentPath $Config.inboxDir "./cloud-inbox"
  New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
  $targetFile = Join-Path $targetDir ("live-{0}" -f (Split-Path -Leaf $CaptureResult.EventFile))
  Copy-Item -LiteralPath $CaptureResult.EventFile -Destination $targetFile -Force

  Push-Location $ProjectRoot
  try {
    $env:VISUAL_AGENT_CONFIG = $ConfigPath
    $nodeExe = "C:\Program Files\nodejs\node.exe"
    if (-not (Test-Path $nodeExe)) {
      $nodeExe = (Get-Command node.exe -ErrorAction Stop).Source
    }
    $output = @(& $nodeExe .\scripts\visual-agent\visual-agent.js --once)
    if ($LASTEXITCODE -ne 0) {
      throw "visual-agent.js fallo con codigo $LASTEXITCODE."
    }
    return [pscustomobject]@{
      Published = $true
      EventFile = $targetFile
      Logs = @($output | ForEach-Object { [string]$_ })
    }
  } catch {
    return [pscustomobject]@{
      Published = $false
      EventFile = $targetFile
      Reason = $_.Exception.Message
    }
  } finally {
    Pop-Location
  }
}

function Normalize-LocalDecisionText {
  param([string]$Value)
  $text = ([string]$Value).ToLowerInvariant().Normalize([System.Text.NormalizationForm]::FormD)
  return ($text -replace "\p{Mn}", "" -replace "\s+", " ").Trim()
}

function Add-LocalDecisionMatch {
  param(
    [object[]]$Matches,
    [string]$Intent,
    [string]$Label,
    [string]$Priority,
    [int]$Score,
    [string[]]$Reasons,
    $Message
  )

  return @($Matches) + [pscustomobject]@{
    Intent = $Intent
    Label = $Label
    Priority = $Priority
    Score = $Score
    Reasons = @($Reasons | Select-Object -Unique)
    Message = $Message
  }
}

function Get-LocalDecisionQueries {
  param($Match)

  $queries = @()
  $message = $Match.Message
  $contactName = [string]$message.contactName
  $conversationType = [string]$message.conversationType
  if ($conversationType -eq "opened_chat" -and $contactName -and $contactName -notmatch "^WhatsApp\s+\d+$") {
    $queries += $contactName
  }

  $text = Normalize-LocalDecisionText ([string]$message.text)
  foreach ($reason in @($Match.Reasons)) {
    if ($reason -and ([string]$reason).Length -ge 4) {
      $queries += [string]$reason
    }
  }

  switch ($Match.Intent) {
    "payment_or_receipt" {
      $queries += @("comprobante", "transferencia", "pago")
    }
    "accounting_debt" {
      $queries += @("saldo", "cuenta", "deuda")
    }
    "price_request" {
      $queries += @("precio", "cuanto", "costo")
    }
  }

  $words = @($text -split "\s+" | Where-Object {
      $_.Length -ge 5 -and
      $_ -notmatch "^(whatsapp|lectura|visual|mensaje|cliente|estado|comprobante|transferencia)$"
    } | Select-Object -First 3)
  $queries += $words

  $seen = @{}
  $unique = @()
  foreach ($query in $queries) {
    $clean = (($query -replace "\s+", " ").Trim())
    if (-not $clean) { continue }
    $key = Normalize-LocalDecisionText $clean
    if (-not $key -or $seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $unique += $clean
  }

  return @($unique | Select-Object -First $IntentMaxQueries)
}

function Invoke-RuleLocalDecisionStep {
  param($BaseCapture)

  $messages = @($BaseCapture.Result.LocalMessages | Where-Object { $_ })
  $localMatches = @()
  foreach ($message in $messages) {
    $text = [string]$message.text
    $normalized = Normalize-LocalDecisionText $text
    if (-not $normalized) { continue }

    $score = 0
    $reasons = @()
    foreach ($keyword in @("pago", "pague", "pagado", "comprobante", "transferencia", "deposito", "payment", "paid", "receipt", "yape", "plin", "nequi", "bancolombia", "banco")) {
      if ($normalized -like "*$keyword*") {
        $score += 2
        $reasons += $keyword
      }
    }
    if ($text -match "\b\d+(?:[.,]\d+)?\s*(usd|usdt|soles|pen|mxn|cop|clp)\b") {
      $score += 3
      $reasons += $matches[1]
    }
    if ($score -ge 3) {
      $localMatches = Add-LocalDecisionMatch -Matches $localMatches -Intent "payment_or_receipt" -Label "Pago / comprobante" -Priority "alta" -Score $score -Reasons $reasons -Message $message
    }

    $score = 0
    $reasons = @()
    foreach ($keyword in @("deuda", "debe", "saldo", "cuenta", "semanal", "reembolso", "rembolso", "devolver", "refund", "balance")) {
      if ($normalized -like "*$keyword*") {
        $score += 2
        $reasons += $keyword
      }
    }
    if ($score -gt 0) {
      $localMatches = Add-LocalDecisionMatch -Matches $localMatches -Intent "accounting_debt" -Label "Cuenta / deuda" -Priority "alta" -Score $score -Reasons $reasons -Message $message
    }

    $score = 0
    $reasons = @()
    foreach ($keyword in @("cuanto", "precio", "costo", "vale", "sale", "cotiza", "cobras", "tarifa", "price", "prices", "cost")) {
      if ($normalized -like "*$keyword*") {
        $score += 2
        $reasons += $keyword
      }
    }
    if ($score -gt 0) {
      $localMatches = Add-LocalDecisionMatch -Matches $localMatches -Intent "price_request" -Label "Pregunta precio" -Priority "media" -Score $score -Reasons $reasons -Message $message
    }
  }

  $ranked = @($localMatches | Sort-Object @{ Expression = "Score"; Descending = $true }, @{ Expression = { if ($_.Priority -eq "alta") { 0 } else { 1 } } })
  if (-not $ranked.Count) {
    return [pscustomobject]@{
      Status = "no_local_action"
      Source = "rules"
      MessageCount = $messages.Count
      Reason = "No detecte pago, deuda o precio en la lectura local."
    }
  }

  $best = $ranked[0]
  $queries = @(Get-LocalDecisionQueries $best)
  return [pscustomobject]@{
    Status = "local_match"
    Source = "rules"
    MessageCount = $messages.Count
    TargetChannel = [string]$best.Message.channelId
    ConversationTitle = [string]$best.Message.contactName
    ConversationType = [string]$best.Message.conversationType
    Text = [string]$best.Message.text
    Intent = $best.Intent
    Label = $best.Label
    Priority = $best.Priority
    Score = $best.Score
    Reasons = $best.Reasons
    Queries = $queries
  }
}

function Get-NodeExe {
  $nodeExe = "C:\Program Files\nodejs\node.exe"
  if (Test-Path $nodeExe) {
    return $nodeExe
  }
  return (Get-Command node.exe -ErrorAction Stop).Source
}

function Invoke-AiLocalDecisionStep {
  param($BaseCapture, $RuleDecision)

  if (-not $env:OPENAI_API_KEY) {
    return $RuleDecision
  }
  if (-not (Test-Path -LiteralPath $LocalDecisionScript)) {
    return $RuleDecision
  }

  $messages = @($BaseCapture.Result.LocalMessages | Where-Object { $_ } | Select-Object -First 60)
  if (-not $messages.Count) {
    return $RuleDecision
  }

  $tempFile = New-TemporaryFile
  try {
    [pscustomobject]@{
      maxQueries = $IntentMaxQueries
      messages = $messages
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempFile.FullName -Encoding UTF8

    $nodeExe = Get-NodeExe
    $output = @(& $nodeExe $LocalDecisionScript --input $tempFile.FullName)
    if ($LASTEXITCODE -ne 0) {
      throw "visual-local-decision.js fallo con codigo $LASTEXITCODE."
    }
    $ai = ($output -join "`n") | ConvertFrom-Json
    if (-not $ai -or $ai.status -ne "local_match") {
      return [pscustomobject]@{
        Status = "no_local_action"
        Source = "openai_local"
        MessageCount = $messages.Count
        Reason = if ($ai.notes) { [string]$ai.notes } else { "OpenAI local no encontro una accion clara." }
      }
    }

    return [pscustomobject]@{
      Status = "local_match"
      Source = "openai_local"
      MessageCount = $messages.Count
      TargetChannel = [string]$ai.targetChannel
      ConversationTitle = [string]$ai.conversationTitle
      ConversationType = [string]$ai.conversationType
      Text = [string]$ai.text
      Intent = [string]$ai.intent
      Label = [string]$ai.label
      Priority = [string]$ai.priority
      Score = [int]$ai.score
      Reasons = @($ai.reasons)
      Queries = @($ai.queries | Select-Object -First $IntentMaxQueries)
      AiNotes = [string]$ai.notes
    }
  } catch {
    $RuleDecision | Add-Member -NotePropertyName AiError -NotePropertyValue $_.Exception.Message -Force
    return $RuleDecision
  } finally {
    Remove-Item -LiteralPath $tempFile.FullName -Force -ErrorAction SilentlyContinue
  }
}

function Invoke-LocalDecisionStep {
  param($BaseCapture)

  $ruleDecision = Invoke-RuleLocalDecisionStep -BaseCapture $BaseCapture
  if ($ruleDecision.Status -eq "local_match") {
    return $ruleDecision
  }

  return Invoke-AiLocalDecisionStep -BaseCapture $BaseCapture -RuleDecision $ruleDecision
}

function Invoke-IntentStep {
  param($LocalDecision)

  if ($Mode -eq "Live" -and $LocalDecision -and $LocalDecision.Status -eq "local_match") {
    $attempts = @()
    foreach ($query in @($LocalDecision.Queries)) {
      $params = @{
        ConfigPath = $ConfigPath
        SkipCloud = $true
        Channel = $LocalDecision.TargetChannel
        Query = $query
        MaxResults = 8
        MaxLinesPerChannel = $MaxLinesPerCapture
        WaitSeconds = $IntentWaitSeconds
      }
      if ($Execute) {
        $params.Execute = $true
        $params.CaptureAfterOpen = $true
      }
      if ($Send -and $Execute) {
        $params.Send = $true
      }

      $result = Invoke-JsonScript -Path $IntentBridgeScript -Parameters $params
      $attempts += [pscustomobject]@{
        Query = $query
        Status = $result.Status
        Selected = $result.Navigation.Selected
        Notes = $result.Notes
      }
      if ($result.Status -in @("preview_match", "executed", "executed_and_captured")) {
        $result | Add-Member -NotePropertyName Source -NotePropertyValue "local_decision" -Force
        $result | Add-Member -NotePropertyName LocalDecisionAttempts -NotePropertyValue $attempts -Force
        return $result
      }
    }

    return [pscustomobject]@{
      Status = "local_no_visible_match"
      Source = "local_decision"
      Execute = [bool]$Execute
      Send = [bool]$Send
      CaptureAfterOpen = [bool]($Execute -or $Send)
      TargetChannel = $LocalDecision.TargetChannel
      CloudInsight = $null
      SelectedQuery = $null
      Queries = $LocalDecision.Queries
      Navigation = $null
      Attempts = $attempts
      Capture = $null
      Notes = @("Decision local detecto $($LocalDecision.Label), pero no encontre una fila visible para abrir en $($LocalDecision.TargetChannel).")
    }
  }

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
    MaxScrollPages = $MaxScrollPages
    ScrollWheelClicks = $ScrollWheelClicks
    HistoryMonths = $HistoryMonths
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

  Write-AutopilotLine "Ciclo ${CycleNumber}: captura base ($Mode)."
  $baseCapture = Invoke-CaptureStep -Name "base_capture"

  $localDecision = Invoke-LocalDecisionStep -BaseCapture $baseCapture

  Write-AutopilotLine "Ciclo ${CycleNumber}: atendiendo alertas clasificadas."
  $intent = $null
  try {
    $intent = Invoke-IntentStep -LocalDecision $localDecision
  } catch {
    $notes += "Intent bridge fallo: $($_.Exception.Message)"
  }

  $basePublish = Publish-CaptureStep -CaptureResult $baseCapture.Result -Config $Config
  if ($basePublish -and -not $basePublish.Published) {
    $notes += "Publicacion nube fallo: $($basePublish.Reason)"
  }

  $learning = $null
  $shouldLearn = $Mode -eq "Full" -and (
    ($LearnOnFirstCycle -and $CycleNumber -eq 1) -or
    ($LearningEveryCycles -gt 0 -and (($CycleNumber % $LearningEveryCycles) -eq 0))
  )
  if ($shouldLearn) {
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
    Mode = $Mode
    Cycle = $CycleNumber
    Execute = [bool]$Execute
    Send = [bool]$Send
    OpenWhatsApp = [bool]$OpenWhatsApp
    ArrangeWindows = [bool]$ArrangeWindows
    OpenedTargets = $openedTargets
    ArrangedWindows = $arrangedWindows
    BaseCapture = $baseCapture
    BasePublish = $basePublish
    LocalDecision = $localDecision
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
  $summaryJson = $summary | ConvertTo-Json -Depth 16
  $summaryJson | Set-Content -LiteralPath $AutopilotStateFile -Encoding UTF8
  $summaryJson

  $shouldContinue = $Watch -and ($MaxCycles -le 0 -or $cycle -lt $MaxCycles)
  if ($shouldContinue) {
    Write-AutopilotLine "Esperando $PollSeconds segundo(s) para el siguiente ciclo."
    $minimumPoll = if ($Mode -eq "Live") { [Math]::Max(1, $LiveMinPollSeconds) } else { 10 }
    Start-Sleep -Seconds ([Math]::Max($minimumPoll, $PollSeconds))
  }
} while ($shouldContinue)

if (-not $Watch) {
  return
}
