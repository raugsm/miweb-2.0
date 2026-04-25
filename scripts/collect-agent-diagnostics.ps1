$ErrorActionPreference = "SilentlyContinue"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $workspaceRoot "data"
$reportPath = Join-Path $dataDir "agent-report.json"

if (-not (Test-Path $dataDir)) {
  New-Item -ItemType Directory -Path $dataDir | Out-Null
}

function New-Result {
  param(
    [string]$Action,
    [string]$Status,
    [string]$Summary,
    [string]$Details
  )

  return [ordered]@{
    action = $Action
    status = $Status
    summary = $Summary
    details = $Details
    checkedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
  }
}

function Get-CommandLocation {
  param([string]$Name)

  $result = Get-Command $Name -ErrorAction SilentlyContinue
  if ($null -eq $result) {
    return $null
  }

  return $result.Source
}

function Get-AdbDiagnostic {
  $adbPath = Get-CommandLocation "adb"

  if (-not $adbPath) {
    return New-Result "adb_devices" "needs_human" "ADB no esta disponible en esta PC." "Instala Android Platform Tools o ajusta PATH antes de reintentar."
  }

  $output = & adb devices 2>&1 | Out-String
  $readyLine = $output -split "`r?`n" | Where-Object { $_ -match "\tdevice$" } | Select-Object -First 1
  $unauthorizedLine = $output -split "`r?`n" | Where-Object { $_ -match "\tunauthorized$" } | Select-Object -First 1

  if ($readyLine) {
    $serial = ($readyLine -split "\t")[0]
    return New-Result "adb_devices" "ok" "ADB detecta al menos un equipo listo: $serial." "Ruta detectada: $adbPath"
  }

  if ($unauthorizedLine) {
    $serial = ($unauthorizedLine -split "\t")[0]
    return New-Result "adb_devices" "needs_human" "ADB ve el equipo $serial, pero falta autorizacion en el telefono." "Pide al cliente aceptar la depuracion USB y vuelve a intentar."
  }

  if ($output -match "cannot connect to daemon|failed to start daemon") {
    return New-Result "adb_devices" "needs_human" "ADB esta instalado, pero el daemon no pudo iniciar correctamente." ($output.Trim())
  }

  return New-Result "adb_devices" "needs_human" "ADB no detecto ningun equipo listo." ($output.Trim())
}

function Get-FastbootDiagnostic {
  $fastbootPath = Get-CommandLocation "fastboot"

  if (-not $fastbootPath) {
    return New-Result "fastboot_devices" "needs_human" "Fastboot no esta disponible en esta PC." "Instala Platform Tools o agrega fastboot al PATH para habilitar esta revision."
  }

  $output = & fastboot devices 2>&1 | Out-String
  $deviceLine = $output -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1

  if ($deviceLine) {
    $serial = ($deviceLine.Trim() -split "\s+")[0]
    return New-Result "fastboot_devices" "ok" "Fastboot detecta $serial." "Ruta detectada: $fastbootPath"
  }

  return New-Result "fastboot_devices" "needs_human" "Fastboot no detecto ningun equipo." ($output.Trim())
}

function Get-UsbDiagnostic {
  $output = & pnputil /enum-devices /connected 2>&1 | Out-String
  $descriptions = @()

  foreach ($line in ($output -split "`r?`n")) {
    $normalizedLine = $line.Normalize([Text.NormalizationForm]::FormD) -replace '\p{Mn}', ''
    if ($normalizedLine -match 'Descripcion del dispositivo:\s*(.+)') {
      $descriptions += $Matches[1].Trim()
    }
  }

  $mobileMatches = $descriptions | Where-Object { $_ -match 'android|xiaomi|redmi|phone|adb|mtp|portable|miui|pixel|samsung' }

  if ($mobileMatches.Count -gt 0) {
    return New-Result "windows_usb_scan" "ok" ("Windows detecta posibles dispositivos moviles: " + ($mobileMatches -join ", ") + ".") ("Dispositivos USB revisados: " + $descriptions.Count)
  }

  $sample = ($descriptions | Select-Object -First 8) -join " | "
  return New-Result "windows_usb_scan" "needs_human" "Windows enumero USB conectados, pero no detecte una firma clara de telefono movil." ("Muestra revisada: " + $sample)
}

$report = [ordered]@{
  generatedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
  adb = Get-AdbDiagnostic
  fastboot = Get-FastbootDiagnostic
  conexion = Get-UsbDiagnostic
}

$json = $report | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($reportPath, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Reporte generado en $reportPath"
