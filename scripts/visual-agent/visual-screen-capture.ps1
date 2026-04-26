param(
  [string]$ConfigPath = "",
  [switch]$Send,
  [switch]$Watch,
  [switch]$FullSection,
  [int]$PollSeconds = 30,
  [int]$MaxLinesPerChannel = 25,
  [ValidateSet("", "wa-1", "wa-2", "wa-3")]
  [string]$ActiveChannel = "",
  [string]$ActiveConversationTitle = "",
  [string]$ActiveConversationId = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Resolve-Path (Join-Path $ScriptDir "..\..")
if (-not $ConfigPath) {
  $ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
}

function Resolve-AgentPath {
  param([string]$Value, [string]$Fallback)
  $target = if ($Value) { $Value } else { $Fallback }
  if ([System.IO.Path]::IsPathRooted($target)) {
    return $target
  }
  return Join-Path $ScriptDir $target
}

function Get-ObjectValue {
  param($Object, [string]$Name, $Fallback)
  if ($Object -and $Object.PSObject.Properties[$Name]) {
    return $Object.$Name
  }
  return $Fallback
}

function Get-RatioValue {
  param($Object, $DefaultObject, [string]$Name, [double]$Fallback)
  $value = Get-ObjectValue $Object $Name (Get-ObjectValue $DefaultObject $Name $Fallback)
  $number = [double]$value
  if ($number -lt 0) { return 0 }
  if ($number -gt 1) { return 1 }
  return $number
}

function Get-Sha256 {
  param([string]$Text)
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$Text)
    return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") }) -join "")
  } finally {
    $sha.Dispose()
  }
}

function Await-Operation {
  param($Operation, [Type]$ResultType)
  $method = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
      $_.Name -eq "AsTask" -and
      $_.IsGenericMethodDefinition -and
      $_.GetParameters().Count -eq 1
    } |
    Select-Object -First 1
  $task = $method.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
  $task.Wait()
  return $task.Result
}

function Initialize-Ocr {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
  $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
  $null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
  $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
  $null = [Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime]
  $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
  if (-not $engine) {
    throw "No pude iniciar Windows OCR. Revisa que Windows tenga un idioma OCR instalado."
  }
  return $engine
}

function Read-OcrLines {
  param([string]$ImagePath, $Engine)
  $file = Await-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
  $stream = Await-Operation ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  try {
    $decoder = Await-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Await-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Await-Operation ($Engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines = @()
    foreach ($line in $result.Lines) {
      $text = (($line.Words | ForEach-Object { $_.Text }) -join " ").Trim()
      if ($text) {
        $lines += $text
      }
    }
    return $lines
  } finally {
    $stream.Dispose()
  }
}

function Test-OcrTimeOnlyLine {
  param([string]$Text)
  $value = ($Text -replace "\s+", " ").Trim()
  $hasClock = $value -match "\b[0-9gqloOiIl]{1,2}\s*[:;]\s*[0-9oO]{2}"
  $hasAmpm = $value -match "\b[apu]\.?\s*(m|rn|nm|urn|um)\.?\b"
  if (-not ($hasClock -or $hasAmpm)) {
    return $false
  }

  $semantic = $value.ToLowerInvariant()
  $semantic = $semantic -replace "\b[0-9gqloOiIl]{1,2}\s*[:;]\s*[0-9oO]{2}", ""
  $semantic = $semantic -replace "\b[apu]\.?\s*(m|rn|nm|urn|um)\.?\b", ""
  $semantic = $semantic -replace "[^a-z0-9\p{L}]+", ""
  return $semantic.Length -le 4
}

function Save-ScreenRegions {
  param([string]$OutputDir, $Channels, $ScreenCapture)
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing

  $screen = [System.Windows.Forms.Screen]::PrimaryScreen
  $bounds = $screen.Bounds
  $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $fullPath = Join-Path $OutputDir "$timestamp-screen.png"
  $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  try {
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
  } finally {
    $graphics.Dispose()
  }

  $regionWidth = [Math]::Floor($bounds.Width / 3)
  $regions = @()
  for ($i = 0; $i -lt 3; $i++) {
    $x = $i * $regionWidth
    $width = if ($i -eq 2) { $bounds.Width - $x } else { $regionWidth }
    $channel = $Channels[$i]
    $channelId = if ($channel.id) { $channel.id } else { "wa-$($i + 1)" }
    $defaultCrop = Get-ObjectValue $ScreenCapture "defaultCrop" $null
    $channelCrop = $null
    $channelCrops = @(Get-ObjectValue $ScreenCapture "channelCrops" @())
    foreach ($candidate in $channelCrops) {
      if ($candidate.id -eq $channelId) {
        $channelCrop = $candidate
        break
      }
    }

    if ($FullSection) {
      $xStartRatio = 0
      $xEndRatio = 1
      $yStartRatio = 0
      $yEndRatio = 1
    } else {
      $xStartRatio = Get-RatioValue $channelCrop $defaultCrop "xStartRatio" 0.42
      $xEndRatio = Get-RatioValue $channelCrop $defaultCrop "xEndRatio" 0.99
      $yStartRatio = Get-RatioValue $channelCrop $defaultCrop "yStartRatio" 0.1
      $yEndRatio = Get-RatioValue $channelCrop $defaultCrop "yEndRatio" 0.94
    }

    if ($xEndRatio -le $xStartRatio) { $xEndRatio = 1 }
    if ($yEndRatio -le $yStartRatio) { $yEndRatio = 1 }

    $cropX = $x + [Math]::Floor($width * $xStartRatio)
    $cropY = [Math]::Floor($bounds.Height * $yStartRatio)
    $cropWidth = [Math]::Floor($width * ($xEndRatio - $xStartRatio))
    $cropHeight = [Math]::Floor($bounds.Height * ($yEndRatio - $yStartRatio))
    $rect = New-Object System.Drawing.Rectangle($cropX, $cropY, $cropWidth, $cropHeight)
    $crop = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
    $cropGraphics = [System.Drawing.Graphics]::FromImage($crop)
    try {
      $cropGraphics.DrawImage($bitmap, 0, 0, $rect, [System.Drawing.GraphicsUnit]::Pixel)
      $path = Join-Path $OutputDir "$timestamp-$channelId.png"
      $crop.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
      $regions += [pscustomobject]@{
        ChannelId = $channelId
        Name = if ($channel.name) { $channel.name } else { "WhatsApp $($i + 1)" }
        Path = $path
      }
    } finally {
      $cropGraphics.Dispose()
      $crop.Dispose()
    }
  }
  $bitmap.Dispose()
  return $regions
}

function Test-UsefulVisualLine {
  param([string]$Text)
  $clean = ($Text -replace "\s+", " ").Trim()
  if ($clean.Length -lt 6) {
    return $false
  }
  if ($clean -notmatch "[\p{L}\p{N}]") {
    return $false
  }
  if (Test-OcrTimeOnlyLine $clean) {
    return $false
  }

  $ignorePatterns = @(
    "^\s*$",
    "^WhatsApp( Business)?$",
    "^WhatsAp+$",
    "^WhatsApp para Windows$",
    "Organiza, administra",
    "mensajes personales est.n cifrados",
    "web\.whatsapp\.com",
    "^WhatsApp$",
    "^usa WhatsApp en tu tel.fono para ver mensajes anteriores",
    "Buscar un chat",
    "iniciar uno nuevo",
    "^\(?\d+\)?\s*WhatsApp",
    "^\d+\s+mensajes?\s+no\s+le.do",
    "^No le.dos$",
    "^Archivados$",
    "^Favoritos$",
    "^Pagos? (Chile|Mexico|M.xico|Colombia|Peru).*$",
    ":\s*(Gl\s*)?Foto$",
    ":\s*(Gl\s*)?Foto\b.*Se fij",
    "\bSe fij[oÃ³ó] el chat\.?$",
    "^Todos$",
    "^Grupos$",
    "^Contactos?$",
    "^Canales$",
    "^Estados$",
    "^T[uú]\s*[•\.-]\s*Estados$",
    "^T[uú].*Estados$",
    "^T.*Estados$",
    "^Comunidades$",
    "^Llamadas$",
    "^Meta AI$",
    "^Llamar$",
    "^Llamadas individuales",
    "Habla en vivo por audio o video",
    "Se paus. la sincronizaci.n",
    "Abre WhatsApp en tu tel.fono",
    "Se activaron los m",
    "Desactivaste los m",
    "mensajes nuevos desaparecer",
    "de este chat despu",
    "opci.n para",
    "Haz dic",
    "Escribe un mensa",
    "^Escribe un m",
    "^Editado\s+\d",
    "^un mensa[lj]e$",
    "^Hoy$",
    "^Ayer$",
    "^mi.rcoles$",
    "^ult\. vez",
    "^Todos los marcadores$",
    "^MVP Soporte$",
    "^Migraci.n",
    "^Codex$",
    "^YouTube$",
    "^\(?\d+\)?\s*YouTube$",
    "^wa\.me/",
    "^directo:wa\.me/",
    "^@[\p{L}\p{N}._-]+$",
    "^Chat silenciado$",
    "Chat silenciado$",
    "^Se fij[oó] el chat\.?$",
    "^[0-9:\s\.apm_]+$",
    "^\d{3,4}\s*[apu]\.?\s*m[_\.]?\s*\w?$",
    "^\d{1,2}:\d{2}\s*[apu]\.?\s*m[_\.]?\s*\w?$",
    "^[A-Z][\p{L}]+(?:\s+[A-Z][\p{L}]+){1,3}\s+(Peru|Chile|Colombia|Mexico|M.xico|Brasil|Argentina)$",
    "^\(?\d+\)?$"
  )

  foreach ($pattern in $ignorePatterns) {
    if ($clean -match $pattern) {
      return $false
    }
  }

  return $true
}

function Test-BlockedVisualSection {
  param([string[]]$Lines)
  $text = (($Lines | ForEach-Object { $_ }) -join " ")
  $blockedPatterns = @(
    "Codex",
    "scripts/visual-agent",
    "visual-screen-capture",
    "capturador",
    "Hagamos esto",
    "Acceso completo",
    "tareas completadas",
    "Ejecutando el comando",
    "Solicitar cambios",
    "Railway",
    "Variables",
    "Deployments",
    "GitHub",
    "YouTube",
    "WhatsApp para Windows",
    "Organiza, administra y haz crecer tu cuenta de empresa",
    "mensajes personales est.n cifrados"
  )

  foreach ($pattern in $blockedPatterns) {
    if ($text -match $pattern) {
      return $true
    }
  }
  return $false
}

function Build-EventsOnce {
  $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
  $channels = @($config.channels)
  if ($channels.Count -lt 3) {
    $channels = @(
      [pscustomobject]@{ id = "wa-1"; name = "WhatsApp 1" },
      [pscustomobject]@{ id = "wa-2"; name = "WhatsApp 2" },
      [pscustomobject]@{ id = "wa-3"; name = "WhatsApp 3" }
    )
  }

  $captureRoot = Join-Path $ScriptDir "captures"
  if (-not $Send) {
    $captureRoot = Join-Path $ScriptDir "captures-dryrun"
  }
  New-Item -ItemType Directory -Force $captureRoot | Out-Null

  $engine = Initialize-Ocr
  $regions = Save-ScreenRegions -OutputDir $captureRoot -Channels $channels -ScreenCapture (Get-ObjectValue $config "screenCapture" $null)
  $now = (Get-Date).ToUniversalTime().ToString("o")
  $dayKey = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
  $events = @()
  $totalLines = 0
  $skippedChannels = @()

  foreach ($region in $regions) {
    $rawLines = @(Read-OcrLines -ImagePath $region.Path -Engine $engine)
    if (Test-BlockedVisualSection $rawLines) {
      $skippedChannels += $region.ChannelId
      continue
    }

    $lines = @()
    foreach ($line in $rawLines) {
      $clean = ($line -replace "\s+", " ").Trim()
      if ($clean -match "^1\s+(\d{2,3})\s+(MXN|PEN|COP|CLP|USD|USDT)$") {
        $clean = "1$($matches[1]) $($matches[2])"
      }
      if ((Test-UsefulVisualLine $clean) -and -not ($lines -contains $clean)) {
        $lines += $clean
      }
    }
    $lines = @($lines | Select-Object -First $MaxLinesPerChannel)
    $captureHash = Get-Sha256 (($lines -join "`n") + "|" + $region.ChannelId)
    $totalLines += $lines.Count
    $conversationTitle = $region.Name
    $conversationType = "visible_screen"
    $conversationId = "visible-screen-$($region.ChannelId)"

    if ($ActiveChannel -eq $region.ChannelId -and $ActiveConversationTitle) {
      $titleHash = (Get-Sha256 "$($region.ChannelId)|$ActiveConversationTitle").Substring(0, 16)
      $conversationTitle = $ActiveConversationTitle
      $conversationType = "opened_chat"
      $conversationId = if ($ActiveConversationId) { $ActiveConversationId } else { "opened-chat-$($region.ChannelId)-$titleHash" }
    }

    foreach ($line in $lines) {
      $messageHash = (Get-Sha256 "$($region.ChannelId)|$dayKey|$line").Substring(0, 18)
      $events += [pscustomobject]@{
        type = "whatsapp_message"
        data = [pscustomobject]@{
          channelId = $region.ChannelId
          contactName = $conversationTitle
          conversationType = $conversationType
          conversationId = $conversationId
          messageKey = "visual-$messageHash"
          senderName = "Lectura visual"
          direction = "unknown"
          text = $line
          sentAt = $now
          dayKey = $dayKey
        }
      }
    }

    $events += [pscustomobject]@{
      type = "checkpoint"
      data = [pscustomobject]@{
        channelId = $region.ChannelId
        conversationId = $conversationId
        dayKey = $dayKey
        lastMessageKey = if ($lines.Count) { "visual-$((Get-Sha256 "$($region.ChannelId)|$dayKey|$($lines[-1])").Substring(0, 18))" } else { $null }
        lastCaptureHash = $captureHash
      }
    }
  }

  $events = @(
    [pscustomobject]@{
      type = "agent_status"
      data = [pscustomobject]@{
        mode = "observador"
        autonomyLevel = 1
        connected = $true
        lastHeartbeat = $now
        message = "Observador visual encontro 3 seccion(es) de pantalla y $totalLines linea(s) utiles." + $(if ($skippedChannels.Count) { " Omitio seccion(es) sin chat util: $($skippedChannels -join ', ')." } else { "" })
      }
    }
  ) + $events

  return [pscustomobject]@{
    Config = $config
    Events = $events
    TotalLines = $totalLines
    CaptureRoot = $captureRoot
  }
}

function Run-Once {
  $result = Build-EventsOnce
  $config = $result.Config
  $targetDir = if ($Send) {
    Resolve-AgentPath $config.inboxDir "./cloud-inbox"
  } else {
    $result.CaptureRoot
  }
  New-Item -ItemType Directory -Force $targetDir | Out-Null
  $eventFile = Join-Path $targetDir ("screen-events-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $result.Events | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $eventFile

  if ($Send) {
    Push-Location $ProjectRoot
    try {
      $env:VISUAL_AGENT_CONFIG = $ConfigPath
      $nodeExe = "C:\Program Files\nodejs\node.exe"
      if (-not (Test-Path $nodeExe)) {
        $nodeExe = (Get-Command node.exe -ErrorAction Stop).Source
      }
      & $nodeExe .\scripts\visual-agent\visual-agent.js --once
      if ($LASTEXITCODE -ne 0) {
        throw "El puente visual-agent.js fallo con codigo $LASTEXITCODE."
      }
    } finally {
      Pop-Location
    }
  }

  [pscustomobject]@{
    Sent = [bool]$Send
    EventFile = $eventFile
    Lines = $result.TotalLines
    CaptureRoot = $result.CaptureRoot
  }
}

do {
  Run-Once
  if ($Watch) {
    Start-Sleep -Seconds $PollSeconds
  }
} while ($Watch)
