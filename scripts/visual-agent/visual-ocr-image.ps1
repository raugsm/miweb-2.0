param(
  [Parameter(Mandatory = $true)]
  [string]$ImagePath,
  [string[]]$Languages = @(),
  [int]$MaxEngines = 3
)

$ErrorActionPreference = "Stop"

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

function Initialize-OcrTypes {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
  $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
  $null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
  $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
  $null = [Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime]
  $null = [Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]
}

function Get-OcrEngines {
  param([string[]]$PreferredLanguages, [int]$Limit)
  Initialize-OcrTypes
  $available = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages)
  if (-not $available -or $available.Count -eq 0) {
    throw "No hay idiomas OCR instalados en Windows."
  }

  $selected = @()
  $preferred = @($PreferredLanguages | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
  if ($preferred.Count -gt 0) {
    foreach ($tag in $preferred) {
      $match = $available | Where-Object { $_.LanguageTag -ieq $tag } | Select-Object -First 1
      if ($match -and -not ($selected | Where-Object { $_.LanguageTag -ieq $match.LanguageTag })) {
        $selected += $match
      }
    }
  }

  if ($selected.Count -eq 0) {
    $profileEngine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if ($profileEngine) {
      return @([pscustomobject]@{
          LanguageTag = $profileEngine.RecognizerLanguage.LanguageTag
          DisplayName = $profileEngine.RecognizerLanguage.DisplayName
          Engine = $profileEngine
        })
    }
    $selected = @($available | Select-Object -First 1)
  }

  $engines = @()
  foreach ($language in ($selected | Select-Object -First ([Math]::Max(1, $Limit)))) {
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new($language.LanguageTag))
    if ($engine) {
      $engines += [pscustomobject]@{
        LanguageTag = $language.LanguageTag
        DisplayName = $language.DisplayName
        Engine = $engine
      }
    }
  }
  if ($engines.Count -eq 0) {
    throw "No pude iniciar ningun motor OCR para los idiomas instalados."
  }
  return $engines
}

function Read-OcrLines {
  param([string]$Path, $Engine)
  $file = Await-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Path)) ([Windows.Storage.StorageFile])
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

function Get-OcrScore {
  param([string[]]$Lines)
  $text = ($Lines -join " ")
  $letters = [regex]::Matches($text, "[\p{L}\p{N}]").Count
  $words = [regex]::Matches($text, "\b[\p{L}\p{N}]{3,}\b").Count
  $noise = [regex]::Matches($text, "[^\p{L}\p{N}\s.,;:?!_@#$%/()-]").Count
  return ($letters + ($words * 6) + ($Lines.Count * 4) - ($noise * 3))
}

$resolved = (Resolve-Path -LiteralPath $ImagePath).Path
$engines = @(Get-OcrEngines -PreferredLanguages $Languages -Limit $MaxEngines)
$results = @()
foreach ($engineInfo in $engines) {
  $lines = @(Read-OcrLines -Path $resolved -Engine $engineInfo.Engine)
  $score = Get-OcrScore -Lines $lines
  $results += [pscustomobject]@{
    LanguageTag = $engineInfo.LanguageTag
    DisplayName = $engineInfo.DisplayName
    Score = $score
    LineCount = $lines.Count
    Lines = $lines
  }
}
$best = $results | Sort-Object Score -Descending | Select-Object -First 1
[pscustomobject]@{
  ImagePath = $resolved
  BestLanguage = $best.LanguageTag
  TriedLanguages = @($results | ForEach-Object {
      [pscustomobject]@{
        LanguageTag = $_.LanguageTag
        DisplayName = $_.DisplayName
        Score = $_.Score
        LineCount = $_.LineCount
      }
    })
  AvailableLanguages = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | ForEach-Object { $_.LanguageTag })
  Lines = @($best.Lines)
} | ConvertTo-Json -Depth 5
