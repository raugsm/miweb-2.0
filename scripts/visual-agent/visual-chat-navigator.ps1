param(
  [ValidateSet("wa-1", "wa-2", "wa-3")]
  [string]$Channel = "wa-2",
  [ValidateSet("List", "Find", "OpenFirst")]
  [string]$Action = "List",
  [string]$Query = "",
  [int]$MaxResults = 12,
  [double]$ListXStartRatio = 0.0,
  [double]$ListXEndRatio = 0.42,
  [double]$YStartRatio = 0.08,
  [double]$YEndRatio = 0.95,
  [int]$MinLineY = 105,
  [switch]$Execute
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RuntimeDir = Join-Path $ScriptDir "runtime"
$CaptureDir = Join-Path $RuntimeDir "chat-navigation"
New-Item -ItemType Directory -Force -Path $CaptureDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime]

$mouseApi = @"
using System;
using System.Runtime.InteropServices;
public class VisualChatMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@
Add-Type -TypeDefinition $mouseApi -ErrorAction SilentlyContinue

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

function Normalize-SearchText {
  param([string]$Value)
  $text = ([string]$Value).ToLowerInvariant().Normalize([System.Text.NormalizationForm]::FormD)
  return ($text -replace "\p{Mn}", "" -replace "\s+", " ").Trim()
}

function Get-ChannelBounds {
  param([string]$ChannelId)
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $index = switch ($ChannelId) {
    "wa-1" { 0 }
    "wa-2" { 1 }
    "wa-3" { 2 }
  }
  $regionWidth = [Math]::Floor($screen.Width / 3)
  $left = $screen.Left + ($regionWidth * $index)
  $right = if ($index -eq 2) { $screen.Right } else { $left + $regionWidth }
  return [pscustomobject]@{
    Channel = $ChannelId
    Left = $left
    Top = $screen.Top
    Right = $right
    Bottom = $screen.Bottom
    Width = $right - $left
    Height = $screen.Height
  }
}

function Save-ChatListCapture {
  param($Bounds)

  $xStart = [Math]::Max([double]0, [Math]::Min([double]1, [double]$ListXStartRatio))
  $xEnd = [Math]::Max([double]0, [Math]::Min([double]1, [double]$ListXEndRatio))
  $yStart = [Math]::Max([double]0, [Math]::Min([double]1, [double]$YStartRatio))
  $yEnd = [Math]::Max([double]0, [Math]::Min([double]1, [double]$YEndRatio))
  if ($xEnd -le $xStart) { $xEnd = 0.42 }
  if ($yEnd -le $yStart) { $yEnd = 0.95 }

  $rect = [System.Drawing.Rectangle]::new(
    [Math]::Floor($Bounds.Left + ($Bounds.Width * $xStart)),
    [Math]::Floor($Bounds.Top + ($Bounds.Height * $yStart)),
    [Math]::Floor($Bounds.Width * ($xEnd - $xStart)),
    [Math]::Floor($Bounds.Height * ($yEnd - $yStart))
  )

  $bitmap = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $path = Join-Path $CaptureDir ("chat-list-{0}-{1}.png" -f $Channel, (Get-Date -Format "yyyyMMdd-HHmmss"))
  try {
    $graphics.CopyFromScreen($rect.Location, [System.Drawing.Point]::Empty, $rect.Size)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  } finally {
    $graphics.Dispose()
    $bitmap.Dispose()
  }

  return [pscustomobject]@{
    Path = $path
    Rect = $rect
  }
}

function Get-WordRectUnion {
  param($Words)
  $rects = @($Words | ForEach-Object { $_.BoundingRect })
  if (-not $rects.Count) {
    return $null
  }
  $left = ($rects | Measure-Object -Property Left -Minimum).Minimum
  $top = ($rects | Measure-Object -Property Top -Minimum).Minimum
  $right = ($rects | Measure-Object -Property Right -Maximum).Maximum
  $bottom = ($rects | Measure-Object -Property Bottom -Maximum).Maximum
  return [pscustomobject]@{
    X = [Math]::Floor($left)
    Y = [Math]::Floor($top)
    Width = [Math]::Ceiling($right - $left)
    Height = [Math]::Ceiling($bottom - $top)
    Left = [Math]::Floor($left)
    Top = [Math]::Floor($top)
    Right = [Math]::Ceiling($right)
    Bottom = [Math]::Ceiling($bottom)
  }
}

function Test-UsefulChatListLine {
  param([string]$Text, $Rect)
  $clean = ($Text -replace "\s+", " ").Trim()
  if ($Rect -and $Rect.Top -lt $MinLineY) { return $false }
  if ($clean.Length -lt 3) { return $false }
  if ($clean -notmatch "[\p{L}\p{N}]") { return $false }
  $ignorePatterns = @(
    "^WhatsApp( Business)?$",
    "^Buscar",
    "^Todos$",
    "^No le.dos$",
    "^Favoritos$",
    "^Archivados$",
    "^Grupos$",
    "^Canales$",
    "^Estados$",
    "^Comunidades$",
    "^Llamadas$",
    "^Meta AI$",
    "^[0-9:\s\.apm]+$"
  )
  foreach ($pattern in $ignorePatterns) {
    if ($clean -match $pattern) { return $false }
  }
  return $true
}

function Get-OcrCandidates {
  param($Capture)

  $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
  if (-not $engine) {
    throw "No pude iniciar Windows OCR. Revisa que Windows tenga idioma OCR instalado."
  }

  $file = Await-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Capture.Path)) ([Windows.Storage.StorageFile])
  $stream = Await-Operation ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  try {
    $decoder = Await-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Await-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Await-Operation ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])

    $rows = @()
    foreach ($line in $result.Lines) {
      $text = (($line.Words | ForEach-Object { $_.Text }) -join " ").Trim()
      $rect = Get-WordRectUnion $line.Words
      if (-not $rect) { continue }
      if (-not (Test-UsefulChatListLine $text $rect)) { continue }

      $screenX = [Math]::Floor($Capture.Rect.Left + $rect.Left + ($rect.Width / 2))
      $screenY = [Math]::Floor($Capture.Rect.Top + $rect.Top + ($rect.Height / 2))
      $rowClickX = [Math]::Floor($Capture.Rect.Left + ($Capture.Rect.Width * 0.50))
      $rows += [pscustomobject]@{
        Channel = $Channel
        Text = $text
        NormalizedText = Normalize-SearchText $text
        Rect = $rect
        Click = [pscustomobject]@{
          X = $rowClickX
          Y = $screenY
        }
        TextCenter = [pscustomobject]@{
          X = $screenX
          Y = $screenY
        }
      }
    }
    return $rows
  } finally {
    $stream.Dispose()
  }
}

function Get-MatchScore {
  param($Candidate, [string]$Search)
  if (-not $Search) {
    return 1
  }
  $normalized = Normalize-SearchText $Search
  $terms = @($normalized -split "\s+" | Where-Object { $_ })
  if (-not $terms.Count) {
    return 1
  }

  $score = 0
  foreach ($term in $terms) {
    if ($Candidate.NormalizedText -like "*$term*") {
      $score += 2
    }
  }
  if ($Candidate.NormalizedText -like "*$normalized*") {
    $score += 4
  }
  return $score
}

function Invoke-Click {
  param([int]$X, [int]$Y)
  [void][VisualChatMouse]::SetCursorPos($X, $Y)
  Start-Sleep -Milliseconds 90
  [VisualChatMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 90
  [VisualChatMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

$bounds = Get-ChannelBounds $Channel
$capture = Save-ChatListCapture $bounds
$candidates = @(Get-OcrCandidates $capture)

$scored = @(
  $candidates |
    ForEach-Object {
      $score = Get-MatchScore $_ $Query
      if ($Action -eq "List" -or $score -gt 0) {
        $_ | Add-Member -NotePropertyName Score -NotePropertyValue $score -Force
        $_
      }
    } |
    Sort-Object Score, { $_.Click.Y } -Descending
)

if ($Action -eq "List") {
  $scored = @($scored | Sort-Object { $_.Click.Y } | Select-Object -First $MaxResults)
} else {
  $scored = @($scored | Sort-Object @{ Expression = "Score"; Descending = $true }, @{ Expression = { $_.Click.Y }; Descending = $false } | Select-Object -First $MaxResults)
}

$selected = if ($Action -eq "OpenFirst" -and $scored.Count) { $scored[0] } else { $null }
if ($selected -and $Execute) {
  Invoke-Click $selected.Click.X $selected.Click.Y
}

[pscustomobject]@{
  Channel = $Channel
  Action = $Action
  Query = $Query
  Execute = [bool]$Execute
  Capture = $capture.Path
  CaptureRect = [pscustomobject]@{
    Left = $capture.Rect.Left
    Top = $capture.Rect.Top
    Width = $capture.Rect.Width
    Height = $capture.Rect.Height
  }
  ChannelBounds = $bounds
  Selected = $selected
  Candidates = $scored
} | ConvertTo-Json -Depth 8
