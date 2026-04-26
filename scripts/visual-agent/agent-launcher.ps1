param(
  [ValidateSet("Gui", "Status", "Start", "Stop", "RunOnce", "HandleIntent", "LearnChats", "StartAutopilot", "StopAutopilot", "AutopilotOnce", "StartEyes", "StopEyes", "EyesSample", "VisualDebug", "OpenPanel", "OpenLocalPanel", "OpenRuntime")]
  [string]$Action = "Gui",
  [int]$PollSeconds = 30,
  [switch]$StartMinimized
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$CaptureScript = Join-Path $ScriptDir "visual-screen-capture.ps1"
$IntentBridgeScript = Join-Path $ScriptDir "visual-intent-bridge.ps1"
$LearningPassScript = Join-Path $ScriptDir "visual-chat-learning-pass.ps1"
$AutopilotScript = Join-Path $ScriptDir "visual-autopilot.ps1"
$PythonAgentScript = Join-Path $ScriptDir "agent-local.py"
$VisualDebuggerScript = Join-Path $ScriptDir "visual-debugger.py"
$EyesStreamScript = Join-Path $ScriptDir "eyes-stream.py"
$ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
$RuntimeDir = Join-Path $ScriptDir "runtime"
$PidFile = Join-Path $RuntimeDir "agent-watch.pid"
$StdoutLog = Join-Path $RuntimeDir "agent-watch.out.log"
$StderrLog = Join-Path $RuntimeDir "agent-watch.err.log"
$AutopilotPidFile = Join-Path $RuntimeDir "agent-autopilot.pid"
$AutopilotOutLog = Join-Path $RuntimeDir "agent-autopilot.out.log"
$AutopilotErrLog = Join-Path $RuntimeDir "agent-autopilot.err.log"
$AutopilotStateFile = Join-Path $RuntimeDir "agent-autopilot.state.json"
$EyesPidFile = Join-Path $RuntimeDir "eyes-stream.pid"
$EyesOutLog = Join-Path $RuntimeDir "eyes-stream.out.log"
$EyesErrLog = Join-Path $RuntimeDir "eyes-stream.err.log"
$EyesStateFile = Join-Path $RuntimeDir "eyes-stream.state.json"

function Ensure-RuntimeDir {
  New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null
}

function Get-PowerShellPath {
  $systemPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
  if (Test-Path $systemPath) {
    return $systemPath
  }
  return (Get-Command powershell.exe -ErrorAction Stop).Source
}

function Get-PythonPath {
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

  throw "No encontre Python. Instala Python o define ARIADGSM_PYTHON con la ruta de python.exe."
}

function Quote-ProcessArgument {
  param([string]$Value)
  if ($null -eq $Value) {
    return '""'
  }
  return '"' + ([string]$Value).Replace('\', '\\').Replace('"', '\"') + '"'
}

function Join-ProcessArguments {
  param([string[]]$Values)
  return (($Values | ForEach-Object { Quote-ProcessArgument $_ }) -join " ")
}

function Quote-CmdArgument {
  param([string]$Value)
  return '"' + ([string]$Value).Replace('"', '\"') + '"'
}

function Start-HiddenProcess {
  param(
    [string[]]$Arguments,
    [switch]$CaptureOutput
  )

  $info = New-Object System.Diagnostics.ProcessStartInfo
  $info.FileName = Get-PowerShellPath
  $info.Arguments = Join-ProcessArguments (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden") + $Arguments)
  $info.WorkingDirectory = $ProjectRoot
  $info.UseShellExecute = $false
  $info.CreateNoWindow = $true
  $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

  if ($CaptureOutput) {
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
  }

  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $info
  [void]$process.Start()
  return $process
}

function Test-AgentConfig {
  if (-not (Test-Path -LiteralPath $ConfigPath)) {
    return [pscustomobject]@{
      Configured = $false
      Message = "Falta visual-agent.cloud.json"
    }
  }

  try {
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $hasToken = [bool]([string]$config.agentToken).Trim()
    return [pscustomobject]@{
      Configured = $hasToken
      CloudUrl = if ($config.cloudUrl) { $config.cloudUrl } else { "https://ariadgsm.com" }
      Message = if ($hasToken) { "Configurado" } else { "Falta agentToken" }
    }
  } catch {
    return [pscustomobject]@{
      Configured = $false
      Message = "Config invalido: $($_.Exception.Message)"
    }
  }
}

function Get-AgentProcess {
  return Get-ProcessFromPidFile $PidFile
}

function Get-AutopilotProcess {
  return Get-ProcessFromPidFile $AutopilotPidFile
}

function Get-EyesProcess {
  return Get-ProcessFromPidFile $EyesPidFile
}

function Get-ProcessFromPidFile {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  $pidText = (Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue | Select-Object -First 1)
  $agentPid = 0
  if (-not [int]::TryParse([string]$pidText, [ref]$agentPid)) {
    return $null
  }

  $process = Get-Process -Id $agentPid -ErrorAction SilentlyContinue
  if (-not $process) {
    return $null
  }
  return $process
}

function Get-LatestFile {
  param([string]$Path, [string]$Filter)
  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }
  return Get-ChildItem -LiteralPath $Path -Filter $Filter -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
}

function Get-AgentStatus {
  Ensure-RuntimeDir
  $configStatus = Test-AgentConfig
  $process = Get-AgentProcess
  $autopilotProcess = Get-AutopilotProcess
  $eyesProcess = Get-EyesProcess
  $latestCapture = Get-LatestFile (Join-Path $ScriptDir "captures") "screen-events-*.json"
  $latestProcessed = Get-LatestFile (Join-Path $ScriptDir "cloud-processed") "*screen-events-*.json"
  $latestError = if (Test-Path -LiteralPath $StderrLog) {
    (Get-Content -LiteralPath $StderrLog -Tail 4 -ErrorAction SilentlyContinue) -join "`n"
  } else {
    ""
  }
  $latestAutopilotError = if (Test-Path -LiteralPath $AutopilotErrLog) {
    (Get-Content -LiteralPath $AutopilotErrLog -Tail 4 -ErrorAction SilentlyContinue) -join "`n"
  } else {
    ""
  }
  $latestAutopilotState = $null
  if (Test-Path -LiteralPath $AutopilotStateFile) {
    try {
      $latestAutopilotState = Get-Content -LiteralPath $AutopilotStateFile -Raw | ConvertFrom-Json
    } catch {
      $latestAutopilotState = $null
    }
  }
  $latestEyesState = $null
  if (Test-Path -LiteralPath $EyesStateFile) {
    try {
      $latestEyesState = Get-Content -LiteralPath $EyesStateFile -Raw | ConvertFrom-Json
    } catch {
      $latestEyesState = $null
    }
  }

  return [pscustomobject]@{
    Running = [bool]$process
    ProcessId = if ($process) { $process.Id } else { $null }
    AutopilotRunning = [bool]$autopilotProcess
    AutopilotProcessId = if ($autopilotProcess) { $autopilotProcess.Id } else { $null }
    EyesRunning = [bool]$eyesProcess
    EyesProcessId = if ($eyesProcess) { $eyesProcess.Id } else { $null }
    Configured = $configStatus.Configured
    ConfigMessage = $configStatus.Message
    CloudUrl = $configStatus.CloudUrl
    PollSeconds = $PollSeconds
    RuntimeDir = $RuntimeDir
    LatestCapture = if ($latestCapture) { $latestCapture.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } else { $null }
    LatestProcessed = if ($latestProcessed) { $latestProcessed.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } else { $null }
    LatestError = $latestError
    LatestAutopilotError = $latestAutopilotError
    LatestAutopilotMode = if ($latestAutopilotState) { $latestAutopilotState.Mode } else { $null }
    LatestAutopilotEngine = if ($latestAutopilotState -and $latestAutopilotState.Engine) { $latestAutopilotState.Engine } else { "powershell" }
    LatestAutopilotCycle = if ($latestAutopilotState) { $latestAutopilotState.Cycle } else { $null }
    LatestAutopilotStatus = if ($latestAutopilotState) { $latestAutopilotState.Status } else { $null }
    LatestAutopilotFinishedAt = if ($latestAutopilotState) { $latestAutopilotState.FinishedAt } else { $null }
    LatestAutopilotLines = if ($latestAutopilotState -and $latestAutopilotState.BaseCapture -and $latestAutopilotState.BaseCapture.Result) { $latestAutopilotState.BaseCapture.Result.Lines } else { $null }
    LatestBasePublished = if ($latestAutopilotState -and $latestAutopilotState.BasePublish) { $latestAutopilotState.BasePublish.Published } else { $null }
    LatestLocalDecision = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision) { $latestAutopilotState.LocalDecision.Status } else { $null }
    LatestLocalSource = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision) { $latestAutopilotState.LocalDecision.Source } else { $null }
    LatestLocalLabel = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision) { $latestAutopilotState.LocalDecision.Label } else { $null }
    LatestLocalChannel = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision) { $latestAutopilotState.LocalDecision.TargetChannel } else { $null }
    LatestLocalText = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision) { $latestAutopilotState.LocalDecision.Text } else { $null }
    LatestLocalQueries = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision -and $latestAutopilotState.LocalDecision.Queries) { (@($latestAutopilotState.LocalDecision.Queries) -join ", ") } else { $null }
    LatestLocalAiError = if ($latestAutopilotState -and $latestAutopilotState.LocalDecision -and $latestAutopilotState.LocalDecision.AiError) { $latestAutopilotState.LocalDecision.AiError } else { "" }
    LatestAutopilotIntent = if ($latestAutopilotState -and $latestAutopilotState.Intent) { $latestAutopilotState.Intent.Status } else { $null }
    LatestIntentSource = if ($latestAutopilotState -and $latestAutopilotState.Intent -and $latestAutopilotState.Intent.Source) { $latestAutopilotState.Intent.Source } else { $null }
    LatestIntentTargetChannel = if ($latestAutopilotState -and $latestAutopilotState.Intent) { $latestAutopilotState.Intent.TargetChannel } else { $null }
    LatestIntentSelectedQuery = if ($latestAutopilotState -and $latestAutopilotState.Intent) { $latestAutopilotState.Intent.SelectedQuery } else { $null }
    LatestIntentNotes = if ($latestAutopilotState -and $latestAutopilotState.Intent -and $latestAutopilotState.Intent.Notes) { (@($latestAutopilotState.Intent.Notes) -join " ") } else { "" }
    LatestAutopilotLearning = if ($latestAutopilotState -and $latestAutopilotState.Learning) { $latestAutopilotState.Learning.Status } else { $null }
    LatestEyesStatus = if ($latestEyesState) { $latestEyesState.Status } else { $null }
    LatestEyesMode = if ($latestEyesState -and $latestEyesState.Mode) { $latestEyesState.Mode } else { $null }
    LatestEyesFrames = if ($latestEyesState) { $latestEyesState.frames } else { $null }
    LatestEyesOcrRuns = if ($latestEyesState) { $latestEyesState.ocrRuns } else { $null }
    LatestEyesPendingOcr = if ($latestEyesState -and $null -ne $latestEyesState.pendingOcr) { $latestEyesState.pendingOcr } else { $null }
    LatestEyesIntervalMs = if ($latestEyesState -and $latestEyesState.captureIntervalMs) { $latestEyesState.captureIntervalMs } else { $null }
    LatestEyesRecordedFrames = if ($latestEyesState -and $null -ne $latestEyesState.recordedFrames) { $latestEyesState.recordedFrames } else { $null }
    LatestEyesStorage = if ($latestEyesState -and $latestEyesState.visionStorageRoot) { $latestEyesState.visionStorageRoot } else { $null }
    LatestEyesUpdatedAt = if ($latestEyesState) { $latestEyesState.updatedAt } else { $null }
    LatestEyesDecision = if ($latestEyesState -and $latestEyesState.lastDecision) { $latestEyesState.lastDecision.Status } else { $null }
    LatestEyesChannel = if ($latestEyesState -and $latestEyesState.lastDecision) { $latestEyesState.lastDecision.TargetChannel } else { $null }
    LatestEyesReport = if ($latestEyesState) { $latestEyesState.report } else { $null }
  }
}

function Get-AgentDiagnosisText {
  param($Status)

  $lines = @()
  if (-not $Status.Configured) {
    $lines += "Falta configuracion: $($Status.ConfigMessage)."
    return ($lines -join "`r`n")
  }

  if ($Status.LatestError) {
    $lines += "Error del observador: $($Status.LatestError)"
  }
  if ($Status.LatestAutopilotError) {
    $lines += "Error del modo vivo: $($Status.LatestAutopilotError)"
  }

  if (-not $Status.LatestAutopilotCycle) {
    $lines += "Todavia no hay ciclos registrados. Pulsa Modo vivo para iniciar lectura rapida."
    return ($lines -join "`r`n")
  }

  $lines += "Ultimo ciclo: motor $($Status.LatestAutopilotEngine), modo $($Status.LatestAutopilotMode), estado $($Status.LatestAutopilotStatus), lineas utiles $($Status.LatestAutopilotLines)."
  if ($null -ne $Status.LatestBasePublished) {
    $lines += "Publicacion nube: " + $(if ($Status.LatestBasePublished) { "completada despues de decidir localmente." } else { "fallo o quedo pendiente." })
  }

  if ($Status.LatestLocalDecision -eq "local_match") {
    $sourceLabel = if ($Status.LatestLocalSource -in @("openai_local", "python_openai")) { "OpenAI local" } elseif ($Status.LatestLocalSource -eq "python_rules") { "Python reglas rapidas" } else { "reglas rapidas" }
    $lines += "Decision local ($sourceLabel): detecte $($Status.LatestLocalLabel) en $($Status.LatestLocalChannel)."
    if ($Status.LatestLocalText) {
      $lines += "Texto: $($Status.LatestLocalText)"
    }
    if ($Status.LatestLocalQueries) {
      $lines += "Busquedas probadas: $($Status.LatestLocalQueries)."
    }
  } elseif ($Status.LatestLocalDecision -eq "no_local_action") {
    $lines += "Decision local: no vi pago, deuda ni precio en la captura actual."
  }

  if ($Status.LatestLocalAiError) {
    $lines += "IA local: fallo OpenAI y use reglas rapidas. $($Status.LatestLocalAiError)"
  }

  switch ($Status.LatestAutopilotIntent) {
    "executed_and_captured" { $lines += "Accion: abri un chat, espere y capture la conversacion." }
    "executed" { $lines += "Accion: abri un chat visible." }
    "preview_match" { $lines += "Accion pendiente: encontre un chat candidato, pero el ciclo estaba en vista previa." }
    "local_no_visible_match" { $lines += "No movi el mouse: la IA local detecto algo, pero no encontro una fila visible para abrir." }
    "preview_no_match" { $lines += "No movi el mouse: no encontre chat visible que coincida con la alerta o busqueda." }
    "no_action" { $lines += "No hubo accion: no hay palabra de busqueda clara." }
    default {
      if ($Status.LatestAutopilotIntent) {
        $lines += "Resultado de alerta: $($Status.LatestAutopilotIntent)."
      }
    }
  }

  if ($Status.LatestIntentNotes) {
    $lines += "Nota: $($Status.LatestIntentNotes)"
  }

  if ($Status.LatestEyesStatus) {
    $lines += "Ojo vivo: $($Status.LatestEyesStatus) $($Status.LatestEyesMode), intervalo $($Status.LatestEyesIntervalMs)ms, frames $($Status.LatestEyesFrames), grabados $($Status.LatestEyesRecordedFrames), OCR $($Status.LatestEyesOcrRuns), pendientes $($Status.LatestEyesPendingOcr), decision $($Status.LatestEyesDecision) $($Status.LatestEyesChannel)."
    if ($Status.LatestEyesStorage) {
      $lines += "Almacen visual: $($Status.LatestEyesStorage)"
    }
  }

  if (-not $Status.AutopilotRunning) {
    $lines += "Modo vivo detenido ahora. Presiona Modo vivo para dejarlo trabajando."
  }

  return ($lines -join "`r`n")
}

function Start-AgentWatch {
  Ensure-RuntimeDir
  $existing = Get-AgentProcess
  if ($existing) {
    return Get-AgentStatus
  }

  $configStatus = Test-AgentConfig
  if (-not $configStatus.Configured) {
    throw $configStatus.Message
  }

  if (-not (Test-Path -LiteralPath $CaptureScript)) {
    throw "No encontre visual-screen-capture.ps1"
  }

  $watchRunner = Join-Path $RuntimeDir "agent-watch-runner.ps1"
  @(
    '$ErrorActionPreference = "Continue"',
    "& '$CaptureScript' -Send -Watch -PollSeconds $PollSeconds *>> '$StdoutLog'"
  ) | Set-Content -LiteralPath $watchRunner -Encoding UTF8

  $commandLine = (Quote-CmdArgument (Get-PowerShellPath)) +
    " -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
    (Quote-CmdArgument $watchRunner)

  $startup = ([WMIClass]"Win32_ProcessStartup").CreateInstance()
  $startup.ShowWindow = 0
  $result = ([WMIClass]"Win32_Process").Create($commandLine, $ProjectRoot, $startup)
  if ($result.ReturnValue -ne 0) {
    throw "No pude iniciar el observador. Win32_Process retorno $($result.ReturnValue)."
  }

  $process = Get-Process -Id $result.ProcessId -ErrorAction Stop

  Set-Content -LiteralPath $PidFile -Value $process.Id -Encoding ASCII
  Start-Sleep -Milliseconds 400
  return Get-AgentStatus
}

function Stop-AgentWatch {
  Ensure-RuntimeDir
  $process = Get-AgentProcess
  if ($process) {
    Stop-Process -Id $process.Id -Force
  }
  if (Test-Path -LiteralPath $PidFile) {
    Remove-Item -LiteralPath $PidFile -Force
  }
  Start-Sleep -Milliseconds 200
  return Get-AgentStatus
}

function Start-Autopilot {
  Ensure-RuntimeDir
  $existing = Get-AutopilotProcess
  if ($existing) {
    return Get-AgentStatus
  }

  $configStatus = Test-AgentConfig
  if (-not $configStatus.Configured) {
    throw $configStatus.Message
  }

  if (-not (Test-Path -LiteralPath $PythonAgentScript)) {
    throw "No encontre agent-local.py"
  }

  $pythonPath = Get-PythonPath
  $runner = Join-Path $RuntimeDir "agent-autopilot-runner.ps1"
  $livePollSeconds = [Math]::Max(3, [Math]::Min(5, $PollSeconds))
  @(
    '$ErrorActionPreference = "Continue"',
    "& '$pythonPath' '$PythonAgentScript' --mode Live --watch --max-cycles 0 --poll-seconds $livePollSeconds --live-min-poll-seconds 3 --execute --send --learning-every-cycles 0 --intent-max-queries 2 --intent-wait-seconds 0.35 --max-lines-per-capture 16 *>> '$AutopilotOutLog'"
  ) | Set-Content -LiteralPath $runner -Encoding UTF8

  $commandLine = (Quote-CmdArgument (Get-PowerShellPath)) +
    " -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
    (Quote-CmdArgument $runner)

  $startup = ([WMIClass]"Win32_ProcessStartup").CreateInstance()
  $startup.ShowWindow = 0
  $result = ([WMIClass]"Win32_Process").Create($commandLine, $ProjectRoot, $startup)
  if ($result.ReturnValue -ne 0) {
    throw "No pude iniciar el autopiloto. Win32_Process retorno $($result.ReturnValue)."
  }

  $process = Get-Process -Id $result.ProcessId -ErrorAction Stop
  Set-Content -LiteralPath $AutopilotPidFile -Value $process.Id -Encoding ASCII
  Start-Sleep -Milliseconds 400
  return Get-AgentStatus
}

function Stop-Autopilot {
  Ensure-RuntimeDir
  $process = Get-AutopilotProcess
  if ($process) {
    Stop-Process -Id $process.Id -Force
  }
  if (Test-Path -LiteralPath $AutopilotPidFile) {
    Remove-Item -LiteralPath $AutopilotPidFile -Force
  }
  Start-Sleep -Milliseconds 200
  return Get-AgentStatus
}

function Start-EyesStream {
  Ensure-RuntimeDir
  $existing = Get-EyesProcess
  if ($existing) {
    return Get-AgentStatus
  }
  if (-not (Test-Path -LiteralPath $EyesStreamScript)) {
    throw "No encontre eyes-stream.py"
  }

  $pythonPath = Get-PythonPath
  $runner = Join-Path $RuntimeDir "eyes-stream-runner.ps1"
  @(
    '$ErrorActionPreference = "Continue"',
    "& '$pythonPath' '$EyesStreamScript' --watch --live --config-path '$ConfigPath' *>> '$EyesOutLog'"
  ) | Set-Content -LiteralPath $runner -Encoding UTF8

  $commandLine = (Quote-CmdArgument (Get-PowerShellPath)) +
    " -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " +
    (Quote-CmdArgument $runner)

  $startup = ([WMIClass]"Win32_ProcessStartup").CreateInstance()
  $startup.ShowWindow = 0
  $result = ([WMIClass]"Win32_Process").Create($commandLine, $ProjectRoot, $startup)
  if ($result.ReturnValue -ne 0) {
    throw "No pude iniciar Ojo vivo. Win32_Process retorno $($result.ReturnValue)."
  }

  $process = Get-Process -Id $result.ProcessId -ErrorAction Stop
  Set-Content -LiteralPath $EyesPidFile -Value $process.Id -Encoding ASCII
  Start-Sleep -Milliseconds 500
  return Get-AgentStatus
}

function Stop-EyesStream {
  Ensure-RuntimeDir
  $process = Get-EyesProcess
  if ($process) {
    Stop-Process -Id $process.Id -Force
  }
  if (Test-Path -LiteralPath $EyesPidFile) {
    Remove-Item -LiteralPath $EyesPidFile -Force
  }
  if (Test-Path -LiteralPath $EyesStateFile) {
    try {
      $eyesState = Get-Content -LiteralPath $EyesStateFile -Raw | ConvertFrom-Json
      $eyesState.Status = "stopped"
      $eyesState.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
      $eyesState | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $EyesStateFile -Encoding UTF8
    } catch {
      # Keep stop best-effort; the process state is more important than report metadata.
    }
  }
  Start-Sleep -Milliseconds 200
  return Get-AgentStatus
}

function Stop-AllAgents {
  Stop-AgentWatch | Out-Null
  Stop-Autopilot | Out-Null
  Stop-EyesStream | Out-Null
  return Get-AgentStatus
}

function Invoke-RunOnce {
  Ensure-RuntimeDir
  $configStatus = Test-AgentConfig
  if (-not $configStatus.Configured) {
    throw $configStatus.Message
  }

  $onceOut = Join-Path $RuntimeDir ("run-once-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $onceErr = Join-Path $RuntimeDir ("run-once-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $process = Start-HiddenProcess -Arguments @("-File", $CaptureScript, "-Send") -CaptureOutput
  $stdout = $process.StandardOutput.ReadToEnd()
  $stderr = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  Set-Content -LiteralPath $onceOut -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $onceErr -Value $stderr -Encoding UTF8

  if ([int]$process.ExitCode -ne 0) {
    $errorText = if ($stderr) { $stderr } else { $stdout }
    throw "La lectura una vez fallo con codigo $($process.ExitCode). $errorText"
  }

  return Get-AgentStatus
}

function Invoke-HandleIntent {
  Ensure-RuntimeDir
  $configStatus = Test-AgentConfig
  if (-not $configStatus.Configured) {
    throw $configStatus.Message
  }
  if (-not (Test-Path -LiteralPath $IntentBridgeScript)) {
    throw "No encontre visual-intent-bridge.ps1"
  }

  $intentOut = Join-Path $RuntimeDir ("intent-bridge-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $intentErr = Join-Path $RuntimeDir ("intent-bridge-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $process = Start-HiddenProcess -Arguments @("-File", $IntentBridgeScript, "-ConfigPath", $ConfigPath, "-Execute", "-CaptureAfterOpen", "-Send") -CaptureOutput
  $stdout = $process.StandardOutput.ReadToEnd()
  $stderr = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  Set-Content -LiteralPath $intentOut -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $intentErr -Value $stderr -Encoding UTF8

  if ([int]$process.ExitCode -ne 0) {
    $errorText = if ($stderr) { $stderr } else { $stdout }
    throw "La atencion de alerta fallo con codigo $($process.ExitCode). $errorText"
  }

  $intentResult = $null
  try {
    $intentResult = $stdout | ConvertFrom-Json
  } catch {
    $intentResult = $null
  }
  if ($intentResult -and $intentResult.Status -notin @("executed", "executed_and_captured")) {
    $noteText = if ($intentResult.Notes) { ($intentResult.Notes -join " ") } else { "" }
    throw "No encontre un chat visible para atender. Estado: $($intentResult.Status). Canal: $($intentResult.TargetChannel). Revisa que los 3 WhatsApp esten visibles. $noteText"
  }

  return Get-AgentStatus
}

function Invoke-LearnChats {
  Ensure-RuntimeDir
  $configStatus = Test-AgentConfig
  if (-not $configStatus.Configured) {
    throw $configStatus.Message
  }
  if (-not (Test-Path -LiteralPath $LearningPassScript)) {
    throw "No encontre visual-chat-learning-pass.ps1"
  }

  $learnOut = Join-Path $RuntimeDir ("chat-learning-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $learnErr = Join-Path $RuntimeDir ("chat-learning-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $process = Start-HiddenProcess -Arguments @("-File", $LearningPassScript, "-ConfigPath", $ConfigPath, "-Execute", "-Send", "-MaxChatsPerChannel", "2", "-MaxLinesPerChat", "40", "-MaxScrollPages", "5", "-ScrollWheelClicks", "6", "-HistoryMonths", "1") -CaptureOutput
  $stdout = $process.StandardOutput.ReadToEnd()
  $stderr = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  Set-Content -LiteralPath $learnOut -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $learnErr -Value $stderr -Encoding UTF8

  if ([int]$process.ExitCode -ne 0) {
    $errorText = if ($stderr) { $stderr } else { $stdout }
    throw "La pasada de aprendizaje fallo con codigo $($process.ExitCode). $errorText"
  }

  $learnResult = $null
  try {
    $learnResult = $stdout | ConvertFrom-Json
  } catch {
    $learnResult = $null
  }
  if ($learnResult -and $learnResult.Status -notin @("executed")) {
    $noteText = if ($learnResult.Notes) { ($learnResult.Notes -join " ") } else { "" }
    throw "No pude abrir chats para aprender. Estado: $($learnResult.Status). Revisa que los 3 WhatsApp esten visibles. $noteText"
  }

  return Get-AgentStatus
}

function Invoke-AutopilotOnce {
  Ensure-RuntimeDir
  $configStatus = Test-AgentConfig
  if (-not $configStatus.Configured) {
    throw $configStatus.Message
  }
  if (-not (Test-Path -LiteralPath $PythonAgentScript)) {
    throw "No encontre agent-local.py"
  }

  $autoOut = Join-Path $RuntimeDir ("autopilot-once-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $autoErr = Join-Path $RuntimeDir ("autopilot-once-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $pythonPath = Get-PythonPath
  $command = "& '$pythonPath' '$PythonAgentScript' --mode Live --max-cycles 1 --execute --send --learning-every-cycles 0 --intent-max-queries 2 --intent-wait-seconds 0.35 --max-lines-per-capture 16"
  $process = Start-HiddenProcess -Arguments @("-Command", $command) -CaptureOutput
  $stdout = $process.StandardOutput.ReadToEnd()
  $stderr = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  Set-Content -LiteralPath $autoOut -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $autoErr -Value $stderr -Encoding UTF8

  if ([int]$process.ExitCode -ne 0) {
    $errorText = if ($stderr) { $stderr } else { $stdout }
    throw "El autopiloto fallo con codigo $($process.ExitCode). $errorText"
  }

  return Get-AgentStatus
}

function Invoke-VisualDebug {
  Ensure-RuntimeDir
  if (-not (Test-Path -LiteralPath $VisualDebuggerScript)) {
    throw "No encontre visual-debugger.py"
  }

  $debugOut = Join-Path $RuntimeDir ("visual-debugger-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $debugErr = Join-Path $RuntimeDir ("visual-debugger-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $pythonPath = Get-PythonPath
  $command = "& '$pythonPath' '$VisualDebuggerScript' --config-path '$ConfigPath' --max-lines-per-capture 40 --intent-max-queries 3 --open"
  $process = Start-HiddenProcess -Arguments @("-Command", $command) -CaptureOutput
  $stdout = $process.StandardOutput.ReadToEnd()
  $stderr = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  Set-Content -LiteralPath $debugOut -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $debugErr -Value $stderr -Encoding UTF8

  if ([int]$process.ExitCode -ne 0) {
    $errorText = if ($stderr) { $stderr } else { $stdout }
    throw "El visual debugger fallo con codigo $($process.ExitCode). $errorText"
  }

  return Get-AgentStatus
}

function Invoke-EyesSample {
  Ensure-RuntimeDir
  if (-not (Test-Path -LiteralPath $EyesStreamScript)) {
    throw "No encontre eyes-stream.py"
  }

  $eyesOut = Join-Path $RuntimeDir ("eyes-sample-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $eyesErr = Join-Path $RuntimeDir ("eyes-sample-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $pythonPath = Get-PythonPath
  $command = "& '$pythonPath' '$EyesStreamScript' --config-path '$ConfigPath' --duration-seconds 8 --live"
  $process = Start-HiddenProcess -Arguments @("-Command", $command) -CaptureOutput
  $stdout = $process.StandardOutput.ReadToEnd()
  $stderr = $process.StandardError.ReadToEnd()
  $process.WaitForExit()
  Set-Content -LiteralPath $eyesOut -Value $stdout -Encoding UTF8
  Set-Content -LiteralPath $eyesErr -Value $stderr -Encoding UTF8

  if ([int]$process.ExitCode -ne 0) {
    $errorText = if ($stderr) { $stderr } else { $stdout }
    throw "La muestra de Ojo vivo fallo con codigo $($process.ExitCode). $errorText"
  }

  return Get-AgentStatus
}

function Open-AgentPanel {
  Start-Process "https://ariadgsm.com/operativa-v2.html"
}

function Open-LocalPanel {
  Start-Process "http://localhost:3000/operativa-v2.html"
}

function Open-RuntimeFolder {
  Ensure-RuntimeDir
  Start-Process explorer.exe $RuntimeDir
}

function Write-StatusJson {
  Get-AgentStatus | ConvertTo-Json -Depth 5
}

function Start-AgentGui {
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing

  [System.Windows.Forms.Application]::EnableVisualStyles()

  $form = New-Object System.Windows.Forms.Form
  $form.Text = "AriadGSM Agent Desktop"
  $form.StartPosition = "CenterScreen"
  $form.Width = 900
  $form.Height = 620
  $form.MinimumSize = New-Object System.Drawing.Size(840, 580)
  $form.BackColor = [System.Drawing.ColorTranslator]::FromHtml("#eef4ff")
  $form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

  $accent = [System.Drawing.ColorTranslator]::FromHtml("#2478f2")
  $accentStrong = [System.Drawing.ColorTranslator]::FromHtml("#0f5fd4")
  $surface = [System.Drawing.ColorTranslator]::FromHtml("#ffffff")
  $surfaceStrong = [System.Drawing.ColorTranslator]::FromHtml("#f4f8ff")
  $ink = [System.Drawing.ColorTranslator]::FromHtml("#101827")
  $muted = [System.Drawing.ColorTranslator]::FromHtml("#61708a")
  $line = [System.Drawing.ColorTranslator]::FromHtml("#d9e3f3")

  $brandPanel = New-Object System.Windows.Forms.Panel
  $brandPanel.Left = 24
  $brandPanel.Top = 18
  $brandPanel.Width = 152
  $brandPanel.Height = 52
  $brandPanel.BackColor = $accent
  $form.Controls.Add($brandPanel)

  $brandA = New-Object System.Windows.Forms.Label
  $brandA.Text = "ARIAD"
  $brandA.Font = New-Object System.Drawing.Font("Arial Black", 24, [System.Drawing.FontStyle]::Italic)
  $brandA.ForeColor = [System.Drawing.Color]::White
  $brandA.AutoSize = $true
  $brandA.Left = 9
  $brandA.Top = 6
  $brandPanel.Controls.Add($brandA)

  $brandG = New-Object System.Windows.Forms.Label
  $brandG.Text = "GSM"
  $brandG.Font = New-Object System.Drawing.Font("Arial Black", 24, [System.Drawing.FontStyle]::Italic)
  $brandG.ForeColor = $accent
  $brandG.AutoSize = $true
  $brandG.Left = 182
  $brandG.Top = 24
  $form.Controls.Add($brandG)

  $title = New-Object System.Windows.Forms.Label
  $title.Text = "Agent Desktop"
  $title.Font = New-Object System.Drawing.Font("Segoe UI", 18, [System.Drawing.FontStyle]::Bold)
  $title.ForeColor = $ink
  $title.AutoSize = $true
  $title.Left = 315
  $title.Top = 20
  $form.Controls.Add($title)

  $subtitle = New-Object System.Windows.Forms.Label
  $subtitle.Text = "Modo vivo local para leer 3 WhatsApp, decidir rapido y reportar a ariadgsm.com"
  $subtitle.Font = New-Object System.Drawing.Font("Segoe UI", 9)
  $subtitle.ForeColor = $muted
  $subtitle.AutoSize = $true
  $subtitle.Left = 318
  $subtitle.Top = 55
  $form.Controls.Add($subtitle)

  $statusTitle = New-Object System.Windows.Forms.Label
  $statusTitle.Text = "Estado"
  $statusTitle.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
  $statusTitle.ForeColor = $ink
  $statusTitle.AutoSize = $true
  $statusTitle.Left = 24
  $statusTitle.Top = 96
  $form.Controls.Add($statusTitle)

  $diagnosisTitle = New-Object System.Windows.Forms.Label
  $diagnosisTitle.Text = "Que paso"
  $diagnosisTitle.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
  $diagnosisTitle.ForeColor = $ink
  $diagnosisTitle.AutoSize = $true
  $diagnosisTitle.Left = 456
  $diagnosisTitle.Top = 96
  $form.Controls.Add($diagnosisTitle)

  $statusBox = New-Object System.Windows.Forms.TextBox
  $statusBox.Left = 24
  $statusBox.Top = 122
  $statusBox.Width = 410
  $statusBox.Height = 210
  $statusBox.Multiline = $true
  $statusBox.ReadOnly = $true
  $statusBox.ScrollBars = "Vertical"
  $statusBox.Font = New-Object System.Drawing.Font("Consolas", 9)
  $statusBox.BackColor = $surface
  $statusBox.ForeColor = $ink
  $statusBox.BorderStyle = "FixedSingle"
  $form.Controls.Add($statusBox)

  $diagnosisBox = New-Object System.Windows.Forms.TextBox
  $diagnosisBox.Left = 456
  $diagnosisBox.Top = 122
  $diagnosisBox.Width = 400
  $diagnosisBox.Height = 210
  $diagnosisBox.Multiline = $true
  $diagnosisBox.ReadOnly = $true
  $diagnosisBox.ScrollBars = "Vertical"
  $diagnosisBox.Font = New-Object System.Drawing.Font("Segoe UI", 9)
  $diagnosisBox.BackColor = $surface
  $diagnosisBox.ForeColor = $ink
  $diagnosisBox.BorderStyle = "FixedSingle"
  $form.Controls.Add($diagnosisBox)

  function Set-AgentButtonStyle {
    param(
      [System.Windows.Forms.Button]$Button,
      [switch]$Primary
    )
    $Button.FlatStyle = "Flat"
    $Button.FlatAppearance.BorderSize = 1
    $Button.FlatAppearance.BorderColor = if ($Primary) { $accentStrong } else { $line }
    $Button.BackColor = if ($Primary) { $accent } else { $surfaceStrong }
    $Button.ForeColor = if ($Primary) { [System.Drawing.Color]::White } else { $ink }
    $Button.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
    $Button.Cursor = [System.Windows.Forms.Cursors]::Hand
  }

  $buttonStart = New-Object System.Windows.Forms.Button
  $buttonStart.Text = "Observador"
  $buttonStart.Left = 24
  $buttonStart.Top = 360
  $buttonStart.Width = 120
  $buttonStart.Height = 34
  Set-AgentButtonStyle $buttonStart
  $form.Controls.Add($buttonStart)

  $buttonAutopilot = New-Object System.Windows.Forms.Button
  $buttonAutopilot.Text = "Modo vivo"
  $buttonAutopilot.Left = 156
  $buttonAutopilot.Top = 360
  $buttonAutopilot.Width = 120
  $buttonAutopilot.Height = 34
  Set-AgentButtonStyle $buttonAutopilot -Primary
  $form.Controls.Add($buttonAutopilot)

  $buttonStop = New-Object System.Windows.Forms.Button
  $buttonStop.Text = "Detener"
  $buttonStop.Left = 288
  $buttonStop.Top = 360
  $buttonStop.Width = 92
  $buttonStop.Height = 34
  Set-AgentButtonStyle $buttonStop
  $form.Controls.Add($buttonStop)

  $buttonOnce = New-Object System.Windows.Forms.Button
  $buttonOnce.Text = "Leer una vez"
  $buttonOnce.Left = 392
  $buttonOnce.Top = 360
  $buttonOnce.Width = 112
  $buttonOnce.Height = 34
  Set-AgentButtonStyle $buttonOnce
  $form.Controls.Add($buttonOnce)

  $buttonPanel = New-Object System.Windows.Forms.Button
  $buttonPanel.Text = "Abrir cabina"
  $buttonPanel.Left = 516
  $buttonPanel.Top = 360
  $buttonPanel.Width = 110
  $buttonPanel.Height = 34
  Set-AgentButtonStyle $buttonPanel
  $form.Controls.Add($buttonPanel)

  $buttonDebug = New-Object System.Windows.Forms.Button
  $buttonDebug.Text = "Ver ojos"
  $buttonDebug.Left = 640
  $buttonDebug.Top = 360
  $buttonDebug.Width = 96
  $buttonDebug.Height = 34
  Set-AgentButtonStyle $buttonDebug
  $form.Controls.Add($buttonDebug)

  $buttonLogs = New-Object System.Windows.Forms.Button
  $buttonLogs.Text = "Logs"
  $buttonLogs.Left = 540
  $buttonLogs.Top = 410
  $buttonLogs.Width = 80
  $buttonLogs.Height = 32
  Set-AgentButtonStyle $buttonLogs
  $form.Controls.Add($buttonLogs)

  $buttonEyes = New-Object System.Windows.Forms.Button
  $buttonEyes.Text = "Ojo vivo"
  $buttonEyes.Left = 640
  $buttonEyes.Top = 410
  $buttonEyes.Width = 96
  $buttonEyes.Height = 32
  Set-AgentButtonStyle $buttonEyes
  $form.Controls.Add($buttonEyes)

  $buttonLocal = New-Object System.Windows.Forms.Button
  $buttonLocal.Text = "Panel local"
  $buttonLocal.Left = 420
  $buttonLocal.Top = 410
  $buttonLocal.Width = 100
  $buttonLocal.Height = 32
  Set-AgentButtonStyle $buttonLocal
  $form.Controls.Add($buttonLocal)

  $buttonIntent = New-Object System.Windows.Forms.Button
  $buttonIntent.Text = "Atender alerta"
  $buttonIntent.Left = 160
  $buttonIntent.Top = 410
  $buttonIntent.Width = 124
  $buttonIntent.Height = 32
  Set-AgentButtonStyle $buttonIntent
  $form.Controls.Add($buttonIntent)

  $buttonLearn = New-Object System.Windows.Forms.Button
  $buttonLearn.Text = "Aprender chats"
  $buttonLearn.Left = 24
  $buttonLearn.Top = 410
  $buttonLearn.Width = 124
  $buttonLearn.Height = 32
  Set-AgentButtonStyle $buttonLearn
  $form.Controls.Add($buttonLearn)

  $hint = New-Object System.Windows.Forms.Label
  $hint.Text = "Nivel actual: lee, decide y abre lecturas. No escribe ni envia mensajes al cliente."
  $hint.AutoSize = $true
  $hint.ForeColor = $muted
  $hint.Left = 24
  $hint.Top = 470
  $form.Controls.Add($hint)

  function Refresh-Ui {
    try {
      $status = Get-AgentStatus
      $lines = @(
        "Observador: " + $(if ($status.Running) { "ACTIVO (PID $($status.ProcessId))" } else { "DETENIDO" }),
        "Modo vivo: " + $(if ($status.AutopilotRunning) { "ACTIVO (PID $($status.AutopilotProcessId))" } else { "DETENIDO" }),
        "Ojo vivo: " + $(if ($status.EyesRunning) { "ACTIVO (PID $($status.EyesProcessId))" } else { "DETENIDO" }),
        "Config: $($status.ConfigMessage)",
        "Nube: $($status.CloudUrl)",
        "Ultima captura: $($status.LatestCapture)",
        "Ultimo envio procesado: $($status.LatestProcessed)",
        "Ultimo modo: $($status.LatestAutopilotMode) motor $($status.LatestAutopilotEngine) ciclo $($status.LatestAutopilotCycle) $($status.LatestAutopilotStatus) | lineas $($status.LatestAutopilotLines) | alerta $($status.LatestAutopilotIntent) | aprendizaje $($status.LatestAutopilotLearning)",
        "Publicacion nube: $($status.LatestBasePublished)",
        "Decision local: $($status.LatestLocalDecision) $($status.LatestLocalSource) $($status.LatestLocalLabel) $($status.LatestLocalChannel)",
        "Ojos: $($status.LatestEyesStatus) $($status.LatestEyesMode) $($status.LatestEyesIntervalMs)ms | frames $($status.LatestEyesFrames) grabados $($status.LatestEyesRecordedFrames) OCR $($status.LatestEyesOcrRuns) pendientes $($status.LatestEyesPendingOcr) | decision $($status.LatestEyesDecision) $($status.LatestEyesChannel)",
        "Almacen visual: $($status.LatestEyesStorage)",
        "Logs: $($status.RuntimeDir)"
      )
      if ($status.LatestError) {
        $lines += ""
        $lines += "Ultimo error observador:"
        $lines += $status.LatestError
      }
      if ($status.LatestAutopilotError) {
        $lines += ""
        $lines += "Ultimo error autopiloto:"
        $lines += $status.LatestAutopilotError
      }
      $statusBox.Text = ($lines -join "`r`n")
      $diagnosisBox.Text = Get-AgentDiagnosisText $status
    } catch {
      $statusBox.Text = "No pude leer estado: $($_.Exception.Message)"
      $diagnosisBox.Text = "No pude construir diagnostico: $($_.Exception.Message)"
    }
  }

  function Run-GuiAction {
    param(
      [scriptblock]$Operation,
      [switch]$MinimizeBefore,
      [switch]$MinimizeAfterSuccess
    )
    $succeeded = $false
    try {
      $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
      if ($MinimizeBefore) {
        $form.WindowState = [System.Windows.Forms.FormWindowState]::Minimized
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 400
      }
      [void]$Operation.Invoke()
      $succeeded = $true
    } catch {
      [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "AriadGSM Agent", "OK", "Warning") | Out-Null
    } finally {
      $form.Cursor = [System.Windows.Forms.Cursors]::Default
      Refresh-Ui
      if ($succeeded -and $MinimizeAfterSuccess) {
        $form.WindowState = [System.Windows.Forms.FormWindowState]::Minimized
      }
    }
  }

  $buttonStart.Add_Click({ Run-GuiAction { Start-AgentWatch | Out-Null } -MinimizeAfterSuccess })
  $buttonAutopilot.Add_Click({ Run-GuiAction { Start-Autopilot | Out-Null } -MinimizeAfterSuccess })
  $buttonStop.Add_Click({ Run-GuiAction { Stop-AllAgents | Out-Null } })
  $buttonOnce.Add_Click({ Run-GuiAction { Invoke-RunOnce | Out-Null } })
  $buttonIntent.Add_Click({ Run-GuiAction { Invoke-HandleIntent | Out-Null } -MinimizeBefore })
  $buttonLearn.Add_Click({ Run-GuiAction { Invoke-LearnChats | Out-Null } -MinimizeBefore })
  $buttonPanel.Add_Click({ Open-AgentPanel })
  $buttonDebug.Add_Click({ Run-GuiAction { Invoke-VisualDebug | Out-Null } })
  $buttonEyes.Add_Click({ Run-GuiAction { Start-EyesStream | Out-Null } -MinimizeAfterSuccess })
  $buttonLocal.Add_Click({ Open-LocalPanel })
  $buttonLogs.Add_Click({ Open-RuntimeFolder })

  $timer = New-Object System.Windows.Forms.Timer
  $timer.Interval = 5000
  $timer.Add_Tick({ Refresh-Ui })
  $timer.Start()

  $form.Add_Shown({
    if ($StartMinimized) {
      $form.WindowState = [System.Windows.Forms.FormWindowState]::Minimized
    }
  })

  Refresh-Ui
  [void]$form.ShowDialog()
}

switch ($Action) {
  "Gui" { Start-AgentGui }
  "Status" { Write-StatusJson }
  "Start" { Start-AgentWatch | ConvertTo-Json -Depth 5 }
  "Stop" { Stop-AllAgents | ConvertTo-Json -Depth 5 }
  "RunOnce" { Invoke-RunOnce | ConvertTo-Json -Depth 5 }
  "HandleIntent" { Invoke-HandleIntent | ConvertTo-Json -Depth 5 }
  "LearnChats" { Invoke-LearnChats | ConvertTo-Json -Depth 5 }
  "StartAutopilot" { Start-Autopilot | ConvertTo-Json -Depth 5 }
  "StopAutopilot" { Stop-Autopilot | ConvertTo-Json -Depth 5 }
  "AutopilotOnce" { Invoke-AutopilotOnce | ConvertTo-Json -Depth 5 }
  "StartEyes" { Start-EyesStream | ConvertTo-Json -Depth 5 }
  "StopEyes" { Stop-EyesStream | ConvertTo-Json -Depth 5 }
  "EyesSample" { Invoke-EyesSample | ConvertTo-Json -Depth 5 }
  "VisualDebug" { Invoke-VisualDebug | ConvertTo-Json -Depth 5 }
  "OpenPanel" { Open-AgentPanel }
  "OpenLocalPanel" { Open-LocalPanel }
  "OpenRuntime" { Open-RuntimeFolder }
}
