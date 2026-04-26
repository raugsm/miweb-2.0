$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$env:VISUAL_AGENT_CONFIG = Join-Path $scriptDir "visual-agent.cloud.json"

Set-Location $projectRoot
node .\scripts\visual-agent\visual-agent.js --once
