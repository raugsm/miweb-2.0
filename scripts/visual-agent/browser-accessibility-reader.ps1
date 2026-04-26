param(
  [string]$ConfigPath = "",
  [string]$PythonPath = "",
  [switch]$Watch,
  [switch]$NoWrite,
  [int]$IntervalMilliseconds = 450,
  [int]$MaxCycles = 1,
  [int]$MaxElementsPerWindow = 1800,
  [int]$MaxMessagesPerWindow = 90
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ConfigPath) {
  $ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
}
$ReaderCoreScript = Join-Path $ScriptDir "reader_core.py"
$RuntimeDir = Join-Path $ScriptDir "runtime"
$ReaderDir = Join-Path $RuntimeDir "reader-core"
$StateFile = Join-Path $ReaderDir "accessibility-reader.state.json"

$SupportedBrowsers = @(
  [pscustomobject]@{ Process = "chrome"; Label = "Google Chrome"; Short = "chrome" },
  [pscustomobject]@{ Process = "msedge"; Label = "Microsoft Edge"; Short = "edge" },
  [pscustomobject]@{ Process = "firefox"; Label = "Mozilla Firefox"; Short = "firefox" }
)

if (Test-Path -LiteralPath $ConfigPath) {
  try {
    $readerConfig = (Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json).readerCore
    if ($readerConfig -and $readerConfig.browserProcesses) {
      $configuredProcesses = @($readerConfig.browserProcesses | ForEach-Object { ([string]$_).ToLowerInvariant() })
      $SupportedBrowsers = @($SupportedBrowsers | Where-Object { $configuredProcesses -contains $_.Process })
    }
    if ($readerConfig -and $readerConfig.accessibilityIntervalMilliseconds -and $IntervalMilliseconds -eq 450) {
      $IntervalMilliseconds = [int]$readerConfig.accessibilityIntervalMilliseconds
    }
  } catch {
  }
}

function Get-UtcIso {
  return (Get-Date).ToUniversalTime().ToString("o")
}

function Ensure-ReaderDir {
  New-Item -ItemType Directory -Force -Path $ReaderDir | Out-Null
}

function Get-PythonPath {
  if ($PythonPath -and (Test-Path -LiteralPath $PythonPath)) {
    return $PythonPath
  }
  if ($env:ARIADGSM_PYTHON -and (Test-Path -LiteralPath $env:ARIADGSM_PYTHON)) {
    return $env:ARIADGSM_PYTHON
  }
  $codexPython = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
  if (Test-Path -LiteralPath $codexPython) {
    return $codexPython
  }
  $python = Get-Command python.exe -ErrorAction SilentlyContinue
  if ($python) {
    return $python.Source
  }
  throw "No encontre Python para alimentar Reader Core."
}

function Normalize-Text {
  param([object]$Value)
  return ([regex]::Replace([string]$Value, "\s+", " ")).Trim()
}

function Remove-Diacritics {
  param([string]$Value)
  if (-not $Value) {
    return ""
  }
  $normalized = $Value.Normalize([Text.NormalizationForm]::FormD)
  $builder = New-Object System.Text.StringBuilder
  foreach ($char in $normalized.ToCharArray()) {
    $category = [Globalization.CharUnicodeInfo]::GetUnicodeCategory($char)
    if ($category -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
      [void]$builder.Append($char)
    }
  }
  return $builder.ToString().Normalize([Text.NormalizationForm]::FormC)
}

function Get-NormalizedKey {
  param([string]$Value)
  return (Remove-Diacritics (Normalize-Text $Value)).ToLowerInvariant()
}

function Test-TimeOnlyLine {
  param([string]$Text)
  $clean = Normalize-Text $Text
  return ($clean -match '^\d{1,2}:\d{2}\s*(a\.?\s*m\.?|p\.?\s*m\.?|am|pm)?\.?\s*u?$')
}

function Test-UiNoiseLine {
  param([string]$Text)
  $clean = Normalize-Text $Text
  if ($clean.Length -lt 3) {
    return $true
  }
  if (Test-TimeOnlyLine $clean) {
    return $true
  }
  if ($clean -match "^(https?://|www\.|web\.whatsapp\.com|chrome://|edge://|about:)") {
    return $true
  }
  $key = Get-NormalizedKey $clean
  $exactNoise = @(
    "whatsapp",
    "google chrome",
    "microsoft edge",
    "mozilla firefox",
    "nueva pestana",
    "new tab",
    "buscar",
    "search",
    "chats",
    "estados",
    "canales",
    "comunidades",
    "llamadas",
    "archivados",
    "todos",
    "no leidos",
    "favoritos",
    "grupos",
    "escribe un mensaje",
    "mensaje",
    "adjuntar",
    "emoji",
    "enviar",
    "minimizar",
    "maximizar",
    "cerrar",
    "atras",
    "adelante",
    "recargar"
  )
  if ($exactNoise -contains $key) {
    return $true
  }
  $noisePatterns = @(
    "usa whatsapp en tu telefono",
    "mensajes anteriores",
    "cifrado de extremo a extremo",
    "pulsa enter para buscar",
    "copiar ruta",
    "barra de direcciones",
    "extensiones",
    "marcadores",
    "preguntar a google",
    "actualiza google chrome",
    "actualiza microsoft edge",
    "restaurar pestana",
    "restaurar pestana"
  )
  foreach ($pattern in $noisePatterns) {
    if ($key.Contains($pattern)) {
      return $true
    }
  }
  return $false
}

function Split-VisibleText {
  param([string]$Text)
  $clean = [string]$Text
  if (-not $clean.Trim()) {
    return @()
  }
  return @($clean -split "(`r`n|`n|`r)") |
    ForEach-Object { Normalize-Text $_ } |
    Where-Object { $_ }
}

function Get-ElementRuntimeKey {
  param([System.Windows.Automation.AutomationElement]$Element)
  try {
    return (($Element.GetRuntimeId() | ForEach-Object { [string]$_ }) -join ".")
  } catch {
    return ""
  }
}

function Get-ElementTextValues {
  param([System.Windows.Automation.AutomationElement]$Element)
  $values = New-Object System.Collections.Generic.List[string]
  try {
    $name = Normalize-Text $Element.Current.Name
    if ($name) {
      $values.Add($name)
    }
  } catch {
  }

  try {
    $valuePattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
      $value = Normalize-Text $valuePattern.Current.Value
      if ($value) {
        $values.Add($value)
      }
    }
  } catch {
  }

  try {
    $legacyPattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern, [ref]$legacyPattern)) {
      foreach ($legacyValue in @($legacyPattern.Current.Name, $legacyPattern.Current.Value, $legacyPattern.Current.Description)) {
        $value = Normalize-Text $legacyValue
        if ($value) {
          $values.Add($value)
        }
      }
    }
  } catch {
  }

  try {
    $textPattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$textPattern)) {
      $value = $textPattern.DocumentRange.GetText(12000)
      foreach ($line in Split-VisibleText $value) {
        $values.Add($line)
      }
    }
  } catch {
  }

  return @($values | Select-Object -Unique)
}

function Get-VisibleLinesFromWindow {
  param(
    [System.Windows.Automation.AutomationElement]$Window,
    [int]$Limit
  )
  $controlTypes = @(
    [System.Windows.Automation.ControlType]::Text,
    [System.Windows.Automation.ControlType]::Edit,
    [System.Windows.Automation.ControlType]::Document,
    [System.Windows.Automation.ControlType]::List,
    [System.Windows.Automation.ControlType]::ListItem,
    [System.Windows.Automation.ControlType]::DataItem,
    [System.Windows.Automation.ControlType]::Hyperlink,
    [System.Windows.Automation.ControlType]::Custom
  )
  $seenElements = @{}
  $items = New-Object System.Collections.Generic.List[object]

  foreach ($controlType in $controlTypes) {
    try {
      $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $controlType
      )
      $found = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    } catch {
      continue
    }

    foreach ($element in $found) {
      if ($items.Count -ge $Limit) {
        break
      }
      $elementKey = Get-ElementRuntimeKey $element
      if ($elementKey -and $seenElements.ContainsKey($elementKey)) {
        continue
      }
      if ($elementKey) {
        $seenElements[$elementKey] = $true
      }
      try {
        $rect = $element.Current.BoundingRectangle
        $offscreen = [bool]$element.Current.IsOffscreen
        if ($offscreen -and ($rect.Width -le 0 -or $rect.Height -le 0)) {
          continue
        }
      } catch {
        continue
      }
      foreach ($value in Get-ElementTextValues $element) {
        foreach ($line in Split-VisibleText $value) {
          if (-not $line) {
            continue
          }
          $items.Add([pscustomobject]@{
              Text = $line
              Left = [double]$rect.Left
              Top = [double]$rect.Top
              Width = [double]$rect.Width
              Height = [double]$rect.Height
            })
        }
      }
    }
  }

  $seenLines = @{}
  return @(
    $items |
      Sort-Object Top, Left |
      ForEach-Object {
        $key = Get-NormalizedKey $_.Text
        if (-not $seenLines.ContainsKey($key)) {
          $seenLines[$key] = $true
          $_.Text
        }
      }
  )
}

function Test-WhatsAppSignature {
  param(
    [string]$Title,
    [string[]]$Lines
  )
  $haystack = Get-NormalizedKey (($Title, $Lines) -join " ")
  $score = 0
  $reasons = New-Object System.Collections.Generic.List[string]

  if ($haystack.Contains("whatsapp")) {
    $score += 5
    $reasons.Add("whatsapp")
  }
  foreach ($pair in @(
      @{ Name = "message_box"; Needles = @("escribe un mensaje", "type a message") },
      @{ Name = "history_notice"; Needles = @("usa whatsapp en tu telefono", "mensajes anteriores") },
      @{ Name = "tabs"; Needles = @("chats", "estados", "canales") },
      @{ Name = "web_title"; Needles = @("web.whatsapp.com", "whatsapp web") }
    )) {
    foreach ($needle in $pair.Needles) {
      if ($haystack.Contains($needle)) {
        $score += 3
        $reasons.Add($pair.Name)
        break
      }
    }
  }

  return [pscustomobject]@{
    Accepted = ($score -ge 5)
    Score = $score
    Reasons = @($reasons | Select-Object -Unique)
    Confidence = if ($score -ge 8) { 0.9 } else { 0.88 }
  }
}

function Get-ConversationTitle {
  param(
    [string]$Title,
    [string]$BrowserLabel
  )
  $clean = Normalize-Text $Title
  $clean = $clean -replace '\s+-\s+Google Chrome$', ""
  $clean = $clean -replace '\s+-\s+Microsoft Edge$', ""
  $clean = $clean -replace '\s+-\s+Mozilla Firefox$', ""
  if (-not $clean -or $clean -match "^(WhatsApp|WhatsApp Web)$") {
    return "WhatsApp $BrowserLabel"
  }
  return $clean
}

function Get-UsefulMessageLines {
  param([string[]]$Lines)
  $messages = New-Object System.Collections.Generic.List[string]
  foreach ($line in $Lines) {
    $clean = Normalize-Text $line
    if (-not $clean) {
      continue
    }
    if (Test-UiNoiseLine $clean) {
      continue
    }
    if ($clean.Length -lt 4) {
      continue
    }
    $messages.Add($clean)
  }
  return @($messages | Select-Object -Last $MaxMessagesPerWindow)
}

function Get-ConfiguredChannels {
  if (-not (Test-Path -LiteralPath $ConfigPath)) {
    return @(
      [pscustomobject]@{ id = "wa-1"; name = "WhatsApp 1" },
      [pscustomobject]@{ id = "wa-2"; name = "WhatsApp 2" },
      [pscustomobject]@{ id = "wa-3"; name = "WhatsApp 3" }
    )
  }
  try {
    $config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($config.channels -and @($config.channels).Count -gt 0) {
      return @($config.channels)
    }
  } catch {
  }
  return @(
    [pscustomobject]@{ id = "wa-1"; name = "WhatsApp 1" },
    [pscustomobject]@{ id = "wa-2"; name = "WhatsApp 2" },
    [pscustomobject]@{ id = "wa-3"; name = "WhatsApp 3" }
  )
}

function Get-BrowserWindowCandidates {
  $processMap = @{}
  foreach ($browser in $SupportedBrowsers) {
    $processMap[$browser.Process] = $browser
  }

  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $children = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
  $candidates = New-Object System.Collections.Generic.List[object]
  foreach ($window in $children) {
    try {
      $pid = [int]$window.Current.ProcessId
      $process = Get-Process -Id $pid -ErrorAction Stop
      $processName = $process.ProcessName.ToLowerInvariant()
      if (-not $processMap.ContainsKey($processName)) {
        continue
      }
      $rect = $window.Current.BoundingRectangle
      if ($rect.Width -le 120 -or $rect.Height -le 120) {
        continue
      }
      $title = Normalize-Text $window.Current.Name
      $lines = Get-VisibleLinesFromWindow -Window $window -Limit $MaxElementsPerWindow
      $signature = Test-WhatsAppSignature -Title $title -Lines $lines
      if (-not $signature.Accepted) {
        continue
      }
      $messages = Get-UsefulMessageLines $lines
      if (@($messages).Count -eq 0) {
        continue
      }
      $browser = $processMap[$processName]
      $candidates.Add([pscustomobject]@{
          ProcessId = $pid
          ProcessName = $processName
          Browser = $browser.Short
          BrowserLabel = $browser.Label
          Title = $title
          ConversationTitle = Get-ConversationTitle -Title $title -BrowserLabel $browser.Label
          Bounds = [pscustomobject]@{
            Left = [double]$rect.Left
            Top = [double]$rect.Top
            Width = [double]$rect.Width
            Height = [double]$rect.Height
          }
          Signature = $signature
          RawVisibleLines = @($lines)
          Messages = @($messages)
        })
    } catch {
      continue
    }
  }
  return @($candidates.ToArray() | Sort-Object { $_.Bounds.Left }, { $_.Bounds.Top } | Select-Object -First 3)
}

function New-ObservationPayloads {
  param([object[]]$Candidates)
  $channels = @(Get-ConfiguredChannels)
  $payloads = New-Object System.Collections.Generic.List[object]
  for ($index = 0; $index -lt @($Candidates).Count; $index++) {
    $candidate = $Candidates[$index]
    $channel = if ($index -lt $channels.Count) { $channels[$index] } else { [pscustomobject]@{ id = "wa-$($index + 1)"; name = "WhatsApp $($index + 1)" } }
    $messages = New-Object System.Collections.Generic.List[object]
    for ($messageIndex = 0; $messageIndex -lt @($candidate.Messages).Count; $messageIndex++) {
      $messages.Add([ordered]@{
          messageId = "uia-$($candidate.ProcessId)-$messageIndex"
          text = $candidate.Messages[$messageIndex]
          senderName = ""
          direction = "unknown"
          sentAt = ""
        })
    }
    $payloads.Add([ordered]@{
        channelId = [string]$channel.id
        source = "accessibility"
        confidence = [double]$candidate.Signature.Confidence
        capturedAt = Get-UtcIso
        conversationTitle = [string]$candidate.ConversationTitle
        visibleOnly = $true
        url = ""
        browser = [string]$candidate.Browser
        browserLabel = [string]$candidate.BrowserLabel
        processId = [int]$candidate.ProcessId
        windowTitle = [string]$candidate.Title
        bounds = $candidate.Bounds
        signature = $candidate.Signature
        rawVisibleLines = @($candidate.RawVisibleLines)
        messages = @($messages)
      })
  }
  return @($payloads.ToArray())
}

function Write-ReaderCoreObservations {
  param([object[]]$Observations)
  if ($NoWrite -or @($Observations).Count -eq 0) {
    return [pscustomobject]@{ Written = 0; Output = "" }
  }
  $python = Get-PythonPath
  $temp = Join-Path $ReaderDir ("accessibility-observations-{0}.json" -f ([guid]::NewGuid().ToString("N")))
  @($Observations) | ConvertTo-Json -Depth 30 -Compress | Set-Content -LiteralPath $temp -Encoding UTF8
  try {
    $output = & $python -B $ReaderCoreScript --write-json-file $temp 2>&1
    if ($LASTEXITCODE -ne 0) {
      throw (($output | Out-String).Trim())
    }
    return [pscustomobject]@{ Written = @($Observations).Count; Output = (($output | Out-String).Trim()) }
  } finally {
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
  }
}

function Write-ReaderState {
  param([object]$State)
  Ensure-ReaderDir
  $State | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $StateFile -Encoding UTF8
}

function Invoke-ReaderCycle {
  param([int]$Cycle)
  Ensure-ReaderDir
  Add-Type -AssemblyName UIAutomationClient
  Add-Type -AssemblyName UIAutomationTypes

  $candidates = @(Get-BrowserWindowCandidates)
  $observations = @(New-ObservationPayloads -Candidates $candidates)
  $writeResult = Write-ReaderCoreObservations -Observations $observations
  $state = [ordered]@{
    Status = if ($Watch) { "running" } else { "completed" }
    Engine = "browser_accessibility_reader"
    Cycle = $Cycle
    updatedAt = Get-UtcIso
    visibleOnly = $true
    noWrite = [bool]$NoWrite
    intervalMilliseconds = $IntervalMilliseconds
    supportedBrowsers = @($SupportedBrowsers | ForEach-Object { $_.Label })
    scannedBrowserProcesses = @($SupportedBrowsers | ForEach-Object { $_.Process })
    whatsappWindows = @($candidates).Count
    observationsWritten = [int]$writeResult.Written
    observations = @($observations | ForEach-Object {
        [ordered]@{
          channelId = $_.channelId
          browser = $_.browser
          conversationTitle = $_.conversationTitle
          messageCount = @($_.messages).Count
          confidence = $_.confidence
          bounds = $_.bounds
        }
      })
    stateFile = $StateFile
    readerCoreFile = Join-Path $ReaderDir "structured-observations.jsonl"
  }
  Write-ReaderState $state
  return $state
}

$cycle = 0
do {
  $cycle += 1
  try {
    $state = Invoke-ReaderCycle -Cycle $cycle
    $state | ConvertTo-Json -Depth 30
  } catch {
    $state = [ordered]@{
      Status = if ($Watch) { "running_with_error" } else { "error" }
      Engine = "browser_accessibility_reader"
      Cycle = $cycle
      updatedAt = Get-UtcIso
      supportedBrowsers = @($SupportedBrowsers | ForEach-Object { $_.Label })
      lastError = $_.Exception.Message
      stateFile = $StateFile
    }
    Write-ReaderState $state
    Write-Error $_.Exception.Message
  }

  if (-not $Watch) {
    break
  }
  if ($MaxCycles -gt 0 -and $cycle -ge $MaxCycles) {
    break
  }
  Start-Sleep -Milliseconds ([Math]::Max(100, $IntervalMilliseconds))
} while ($true)
