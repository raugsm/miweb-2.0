param(
  [string[]]$Languages = @("es-MX", "en-US", "pt-BR", "fr-FR", "de-DE", "it-IT"),
  [switch]$AllAvailable,
  [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

$NormalizedLanguages = @(
  $Languages |
    ForEach-Object { ([string]$_).Split(",") } |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ }
)

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]::new($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-Argument {
  param([string]$Value)
  return '"' + ([string]$Value).Replace('"', '\"') + '"'
}

if (-not (Test-IsAdministrator)) {
  if ($NoElevate) {
    throw "Instalar idiomas OCR requiere abrir PowerShell como administrador."
  }

  $argsList = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Quote-Argument $PSCommandPath)
  )
  if ($AllAvailable) {
    $argsList += "-AllAvailable"
  } else {
    $argsList += "-Languages"
    $argsList += ($NormalizedLanguages | ForEach-Object { Quote-Argument $_ })
  }
  $argsList += "-NoElevate"
  Start-Process -FilePath "powershell.exe" -ArgumentList ($argsList -join " ") -Verb RunAs
  [pscustomobject]@{
    Status = "elevation_requested"
    Message = "Se abrio una ventana/UAC de administrador para instalar idiomas OCR."
    Languages = $NormalizedLanguages
    AllAvailable = [bool]$AllAvailable
  } | ConvertTo-Json -Depth 5
  exit 0
}

$targets = @()
if ($AllAvailable) {
  $targets = @(Get-WindowsCapability -Online -Name "Language.OCR*" | Where-Object { $_.State -ne "Installed" })
} else {
  foreach ($language in $NormalizedLanguages) {
    $tag = ([string]$language).Trim()
    if (-not $tag) { continue }
    $name = "Language.OCR~~~$tag~0.0.1.0"
    $capability = Get-WindowsCapability -Online -Name $name -ErrorAction SilentlyContinue
    if ($capability) {
      $targets += $capability
    } else {
      $targets += [pscustomobject]@{ Name = $name; State = "NotFound" }
    }
  }
}

$results = @()
foreach ($target in $targets) {
  if ($target.State -eq "Installed") {
    $results += [pscustomobject]@{ Name = $target.Name; Status = "already_installed" }
    continue
  }
  if ($target.State -eq "NotFound") {
    $results += [pscustomobject]@{ Name = $target.Name; Status = "not_found" }
    continue
  }
  try {
    $installed = Add-WindowsCapability -Online -Name $target.Name
    $results += [pscustomobject]@{ Name = $target.Name; Status = "installed"; RestartNeeded = $installed.RestartNeeded }
  } catch {
    $results += [pscustomobject]@{ Name = $target.Name; Status = "error"; Error = $_.Exception.Message }
  }
}

[pscustomobject]@{
  Status = "completed"
  InstalledAt = (Get-Date).ToUniversalTime().ToString("o")
  Results = $results
} | ConvertTo-Json -Depth 6
