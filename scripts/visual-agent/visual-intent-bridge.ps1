param(
  [string]$ConfigPath = "",
  [ValidateSet("auto", "wa-1", "wa-2", "wa-3")]
  [string]$Channel = "auto",
  [ValidateSet(
    "payment_or_receipt",
    "accounting_debt",
    "price_request",
    "device_or_imei",
    "technical_process",
    "provider_offer",
    "procedure_reference",
    "process_update"
  )]
  [string[]]$Intents = @("payment_or_receipt", "accounting_debt", "price_request"),
  [string]$Query = "",
  [int]$MaxResults = 8,
  [int]$MaxQueries = 8,
  [double]$WaitSeconds = 0.8,
  [int]$MaxLinesPerChannel = 30,
  [switch]$Execute,
  [switch]$CaptureAfterOpen,
  [switch]$Send,
  [switch]$SkipCloud
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$NavigatorScript = Join-Path $ScriptDir "visual-chat-navigator.ps1"
$CaptureScript = Join-Path $ScriptDir "visual-screen-capture.ps1"
if (-not $ConfigPath) {
  $ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
}

if ($Send) {
  $CaptureAfterOpen = $true
}

$mouseApi = @"
using System;
using System.Runtime.InteropServices;
public class VisualBridgeMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@
Add-Type -TypeDefinition $mouseApi -ErrorAction SilentlyContinue

function Normalize-BridgeText {
  param([string]$Value)
  $text = ([string]$Value).ToLowerInvariant().Normalize([System.Text.NormalizationForm]::FormD)
  return ($text -replace "\p{Mn}", "" -replace "\s+", " ").Trim()
}

function Limit-Text {
  param([string]$Value, [int]$Length = 220)
  $text = (($Value -replace "\s+", " ").Trim())
  if ($text.Length -le $Length) {
    return $text
  }
  return $text.Substring(0, $Length).Trim() + "..."
}

function Test-SkipBridgeChatText {
  param([string]$Value)
  $normalized = Normalize-BridgeText $Value
  if ($normalized -match "\bpagos?\b.*\b(mexico|chile|colombia|peru|mx|cl|co|pe)\b") {
    return $true
  }
  return $false
}

function Get-BridgeRowText {
  param($Navigation, $Candidate)
  $candidateY = [int]$Candidate.Click.Y
  $row = @($Navigation.Candidates |
    Where-Object {
      $_.Click -and [Math]::Abs(([int]$_.Click.Y) - $candidateY) -le 36
    } |
    Sort-Object { $_.Rect.Left })
  return (($row | ForEach-Object { [string]$_.Text }) -join " " -replace "\s+", " ").Trim()
}

function Test-BridgeCandidateAllowed {
  param($Navigation, $Candidate)
  if (-not $Candidate) { return $false }
  $candidateText = [string]$Candidate.Text
  $rowText = Get-BridgeRowText -Navigation $Navigation -Candidate $Candidate
  if (Test-SkipBridgeChatText $candidateText) { return $false }
  if (Test-SkipBridgeChatText $rowText) { return $false }
  return $true
}

function Select-BridgeNavigationCandidate {
  param($Navigation)
  foreach ($candidate in @($Navigation.Candidates)) {
    if (Test-BridgeCandidateAllowed -Navigation $Navigation -Candidate $candidate) {
      return $candidate
    }
  }
  return $null
}

function Invoke-BridgeClick {
  param([int]$X, [int]$Y)
  [void][VisualBridgeMouse]::SetCursorPos($X, $Y)
  Start-Sleep -Milliseconds 90
  [VisualBridgeMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 90
  [VisualBridgeMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Get-UniqueQueries {
  param([string[]]$Values, [int]$Limit)
  $seen = @{}
  $items = @()
  foreach ($value in $Values) {
    $clean = (($value -replace "\s+", " ").Trim())
    if (-not $clean) { continue }
    $key = Normalize-BridgeText $clean
    if (-not $key -or $seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $items += $clean
    if ($items.Count -ge $Limit) { break }
  }
  return $items
}

function Read-AgentConfig {
  if (Test-Path -LiteralPath $ConfigPath) {
    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
  }

  return [pscustomobject]@{
    cloudUrl = "https://ariadgsm.com"
    agentToken = ""
  }
}

function Invoke-CloudSnapshot {
  param($Config)
  $token = ([string]$Config.agentToken).Trim()
  if (-not $token) {
    throw "Falta agentToken en la configuracion local."
  }

  $cloudUrl = ([string]$Config.cloudUrl).Trim()
  if (-not $cloudUrl) {
    $cloudUrl = "https://ariadgsm.com"
  }
  $cloudUrl = $cloudUrl.TrimEnd("/")

  return Invoke-RestMethod -Method Get -Uri "$cloudUrl/api/operativa-v2" -Headers @{
    Authorization = "Bearer $token"
  } -TimeoutSec 20
}

function Select-IntentInsight {
  param($Snapshot)
  $rows = @($Snapshot.messageInsights)
  foreach ($row in $rows) {
    $intent = [string]$row.classification.intent
    $rowChannel = [string]$row.channelId
    if (($Intents -contains $intent) -and ($Channel -eq "auto" -or $Channel -eq $rowChannel)) {
      return $row
    }
  }
  return $null
}

function Get-IntentFallbackQueries {
  param([string]$Intent)
  switch ($Intent) {
    "payment_or_receipt" { return @("pago", "comprobante", "transferencia", "yape", "plin") }
    "accounting_debt" { return @("deuda", "saldo", "cuenta", "reembolso", "rembolso") }
    "price_request" { return @("precio", "cuanto", "sale", "vale", "costo") }
    "device_or_imei" { return @("imei", "modelo", "motorola", "samsung", "xiaomi", "iphone") }
    "technical_process" { return @("frp", "unlock", "liberacion", "reset", "firmware") }
    "provider_offer" { return @("proveedor", "service price update", "lista blanca", "instant success") }
    "procedure_reference" { return @("youtube", "drive", "procedimiento", "descargar", "archivo") }
    "process_update" { return @("listo", "done", "pendiente", "proceso", "completado") }
    default { return @() }
  }
}

function Get-TextSignalQueries {
  param($Insight)
  if (-not $Insight) {
    return @()
  }

  $text = Normalize-BridgeText ([string]$Insight.text)
  $signals = @()
  $knownSignals = @(
    "pago",
    "pague",
    "pagado",
    "comprobante",
    "transferencia",
    "deposito",
    "yape",
    "plin",
    "deuda",
    "saldo",
    "cuenta",
    "reembolso",
    "rembolso",
    "precio",
    "cuanto",
    "sale",
    "vale",
    "costo",
    "imei",
    "modelo",
    "frp",
    "unlock",
    "liberacion",
    "reset"
  )

  foreach ($signal in $knownSignals) {
    if ($text -like "*$signal*") {
      $signals += $signal
    }
  }

  foreach ($reason in @($Insight.classification.reasons)) {
    if ($reason -and ([string]$reason).Length -le 32 -and ([string]$reason) -notmatch "^/") {
      $signals += [string]$reason
    }
  }

  $words = @($text -split "\s+" | Where-Object {
      $_.Length -ge 5 -and
      $_ -notmatch "^(cliente|lectura|visual|mensaje|whatsapp|business|estado|chat|para|este|esta|tiene|hacer|favor)$"
    } | Select-Object -First 3)
  $signals += $words

  return $signals
}

function Build-Queries {
  param($Insight)
  if ($Query) {
    return Get-UniqueQueries @($Query) 1
  }

  $values = @()
  $values += Get-TextSignalQueries $Insight
  if ($Insight -and $Insight.classification.intent) {
    $values += Get-IntentFallbackQueries ([string]$Insight.classification.intent)
  }
  foreach ($intent in $Intents) {
    $values += Get-IntentFallbackQueries $intent
  }

  return Get-UniqueQueries $values $MaxQueries
}

function Invoke-Navigator {
  param(
    [string]$TargetChannel,
    [string]$SearchQuery,
    [switch]$DoExecute
  )

  if (-not (Test-Path -LiteralPath $NavigatorScript)) {
    throw "No encontre visual-chat-navigator.ps1"
  }

  $navigatorParams = @{
    Channel = $TargetChannel
    Action = "OpenFirst"
    Query = $SearchQuery
    MaxResults = $MaxResults
  }
  if ($DoExecute) {
    $navigatorParams.Execute = $true
  }

  $output = & $NavigatorScript @navigatorParams
  $json = ($output | Out-String).Trim()
  if (-not $json) {
    throw "El navegador visual no devolvio resultado."
  }
  return $json | ConvertFrom-Json
}

function Invoke-CaptureAfterNavigation {
  if (-not (Test-Path -LiteralPath $CaptureScript)) {
    throw "No encontre visual-screen-capture.ps1"
  }

  $captureParams = @{
    ConfigPath = $ConfigPath
    MaxLinesPerChannel = $MaxLinesPerChannel
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
    Result = $result
    Logs = $logs
  }
}

function Get-InsightSummary {
  param($Insight)
  if (-not $Insight) {
    return $null
  }
  return [pscustomobject]@{
    id = $Insight.id
    channelId = $Insight.channelId
    conversationId = $Insight.conversationId
    conversationTitle = $Insight.conversationTitle
    text = Limit-Text ([string]$Insight.text)
    intent = $Insight.classification.intent
    label = $Insight.classification.label
    priority = $Insight.classification.priority
    confidence = $Insight.classification.confidence
    suggestedAction = $Insight.suggestedAction
  }
}

$config = Read-AgentConfig
$snapshot = $null
$cloudError = $null
$insight = $null

if (-not $SkipCloud -and -not $Query) {
  try {
    $snapshot = Invoke-CloudSnapshot $config
    $insight = Select-IntentInsight $snapshot
  } catch {
    $cloudError = $_.Exception.Message
  }
}

$targetChannel = if ($Channel -ne "auto") {
  $Channel
} elseif ($insight -and $insight.channelId) {
  [string]$insight.channelId
} else {
  "wa-2"
}

$queries = @(Build-Queries $insight)
$attempts = @()
$navigation = $null
$selectedQuery = $null
$capture = $null
$notes = @()

if ($cloudError) {
  $notes += "No pude consultar la cabina: $cloudError"
}
if (-not $insight -and -not $Query) {
  $notes += "No encontre mensajes recientes con las intenciones solicitadas."
}
if (-not $queries.Count) {
  $notes += "No hay una palabra de busqueda para navegar."
}

foreach ($candidateQuery in $queries) {
  $candidateNavigation = Invoke-Navigator -TargetChannel $targetChannel -SearchQuery $candidateQuery
  $allowedSelection = Select-BridgeNavigationCandidate $candidateNavigation
  $candidateNavigation.Selected = $allowedSelection
  $skippedCount = @($candidateNavigation.Candidates | Where-Object { -not (Test-BridgeCandidateAllowed -Navigation $candidateNavigation -Candidate $_) }).Count
  $attempts += [pscustomobject]@{
    Query = $candidateQuery
    CandidateCount = @($candidateNavigation.Candidates).Count
    SkippedPaymentGroups = $skippedCount
    Selected = $allowedSelection
    Capture = $candidateNavigation.Capture
  }

  if ($allowedSelection) {
    $navigation = $candidateNavigation
    $selectedQuery = $candidateQuery
    break
  }
}

if ($navigation -and $Execute) {
  Invoke-BridgeClick -X ([int]$navigation.Selected.Click.X) -Y ([int]$navigation.Selected.Click.Y)
  if ($CaptureAfterOpen) {
    Start-Sleep -Seconds ([Math]::Max(0, $WaitSeconds))
    $capture = Invoke-CaptureAfterNavigation
  }
} elseif ($CaptureAfterOpen) {
  $notes += "La captura posterior se omitio porque el script esta en modo preview sin -Execute."
}

$status = if ($navigation -and $Execute -and $capture) {
  "executed_and_captured"
} elseif ($navigation -and $Execute) {
  "executed"
} elseif ($navigation) {
  "preview_match"
} elseif ($queries.Count) {
  "preview_no_match"
} else {
  "no_action"
}

[pscustomobject]@{
  Status = $status
  Execute = [bool]$Execute
  Send = [bool]$Send
  CaptureAfterOpen = [bool]$CaptureAfterOpen
  TargetChannel = $targetChannel
  Intents = $Intents
  CloudInsight = Get-InsightSummary $insight
  SelectedQuery = $selectedQuery
  Queries = $queries
  Navigation = $navigation
  Attempts = $attempts
  Capture = $capture
  Notes = $notes
} | ConvertTo-Json -Depth 10
