$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $scriptDir "cloud-test-ready\001-smoke-whatsapp-message.json"
$targetDir = Join-Path $scriptDir "cloud-inbox"
$target = Join-Path $targetDir "001-smoke-whatsapp-message.json"

New-Item -ItemType Directory -Force $targetDir | Out-Null
Copy-Item -Force $source $target

& (Join-Path $scriptDir "run-cloud-once.ps1")
