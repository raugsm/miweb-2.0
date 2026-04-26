param(
  [ValidateSet("Gui", "Status", "Start", "Stop", "RunOnce", "HandleIntent", "LearnChats", "StartAutopilot", "StopAutopilot", "AutopilotOnce", "OpenPanel", "OpenLocalPanel", "OpenRuntime")]
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
$ConfigPath = Join-Path $ScriptDir "visual-agent.cloud.json"
$RuntimeDir = Join-Path $ScriptDir "runtime"
$PidFile = Join-Path $RuntimeDir "agent-watch.pid"
$StdoutLog = Join-Path $RuntimeDir "agent-watch.out.log"
$StderrLog = Join-Path $RuntimeDir "agent-watch.err.log"
$AutopilotPidFile = Join-Path $RuntimeDir "agent-autopilot.pid"
$AutopilotOutLog = Join-Path $RuntimeDir "agent-autopilot.out.log"
$AutopilotErrLog = Join-Path $RuntimeDir "agent-autopilot.err.log"
$AutopilotStateFile = Join-Path $RuntimeDir "agent-autopilot.state.json"

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

  return [pscustomobject]@{
    Running = [bool]$process
    ProcessId = if ($process) { $process.Id } else { $null }
    AutopilotRunning = [bool]$autopilotProcess
    AutopilotProcessId = if ($autopilotProcess) { $autopilotProcess.Id } else { $null }
    Configured = $configStatus.Configured
    ConfigMessage = $configStatus.Message
    CloudUrl = $configStatus.CloudUrl
    PollSeconds = $PollSeconds
    RuntimeDir = $RuntimeDir
    LatestCapture = if ($latestCapture) { $latestCapture.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } else { $null }
    LatestProcessed = if ($latestProcessed) { $latestProcessed.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } else { $null }
    LatestError = $latestError
    LatestAutopilotError = $latestAutopilotError
    LatestAutopilotCycle = if ($latestAutopilotState) { $latestAutopilotState.Cycle } else { $null }
    LatestAutopilotStatus = if ($latestAutopilotState) { $latestAutopilotState.Status } else { $null }
    LatestAutopilotFinishedAt = if ($latestAutopilotState) { $latestAutopilotState.FinishedAt } else { $null }
    LatestAutopilotLines = if ($latestAutopilotState -and $latestAutopilotState.BaseCapture -and $latestAutopilotState.BaseCapture.Result) { $latestAutopilotState.BaseCapture.Result.Lines } else { $null }
    LatestAutopilotIntent = if ($latestAutopilotState -and $latestAutopilotState.Intent) { $latestAutopilotState.Intent.Status } else { $null }
    LatestAutopilotLearning = if ($latestAutopilotState -and $latestAutopilotState.Learning) { $latestAutopilotState.Learning.Status } else { $null }
  }
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

  if (-not (Test-Path -LiteralPath $AutopilotScript)) {
    throw "No encontre visual-autopilot.ps1"
  }

  $runner = Join-Path $RuntimeDir "agent-autopilot-runner.ps1"
  @(
    '$ErrorActionPreference = "Continue"',
    "& '$AutopilotScript' -Watch -MaxCycles 0 -PollSeconds $PollSeconds -Execute -Send -LearningEveryCycles 6 -IntentMaxQueries 3 -MaxLinesPerCapture 20 -MaxLinesPerChat 25 *>> '$AutopilotOutLog'"
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

function Stop-AllAgents {
  Stop-AgentWatch | Out-Null
  Stop-Autopilot | Out-Null
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
  $process = Start-HiddenProcess -Arguments @("-File", $LearningPassScript, "-ConfigPath", $ConfigPath, "-Execute", "-Send", "-MaxChatsPerChannel", "2", "-MaxLinesPerChat", "40") -CaptureOutput
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
  if (-not (Test-Path -LiteralPath $AutopilotScript)) {
    throw "No encontre visual-autopilot.ps1"
  }

  $autoOut = Join-Path $RuntimeDir ("autopilot-once-{0}.out.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $autoErr = Join-Path $RuntimeDir ("autopilot-once-{0}.err.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
  $process = Start-HiddenProcess -Arguments @("-File", $AutopilotScript, "-MaxCycles", "1", "-Execute", "-Send", "-LearningEveryCycles", "6", "-IntentMaxQueries", "3", "-MaxLinesPerCapture", "20", "-MaxLinesPerChat", "25") -CaptureOutput
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
  $form.Width = 700
  $form.Height = 470
  $form.MinimumSize = New-Object System.Drawing.Size(660, 430)

  $title = New-Object System.Windows.Forms.Label
  $title.Text = "AriadGSM Agent Desktop"
  $title.Font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
  $title.AutoSize = $true
  $title.Left = 22
  $title.Top = 18
  $form.Controls.Add($title)

  $subtitle = New-Object System.Windows.Forms.Label
  $subtitle.Text = "Observador local para leer los 3 WhatsApp y enviar eventos a ariadgsm.com"
  $subtitle.Font = New-Object System.Drawing.Font("Segoe UI", 9)
  $subtitle.AutoSize = $true
  $subtitle.Left = 24
  $subtitle.Top = 55
  $form.Controls.Add($subtitle)

  $statusBox = New-Object System.Windows.Forms.TextBox
  $statusBox.Left = 24
  $statusBox.Top = 88
  $statusBox.Width = 640
  $statusBox.Height = 165
  $statusBox.Multiline = $true
  $statusBox.ReadOnly = $true
  $statusBox.ScrollBars = "Vertical"
  $statusBox.Font = New-Object System.Drawing.Font("Consolas", 9)
  $form.Controls.Add($statusBox)

  $buttonStart = New-Object System.Windows.Forms.Button
  $buttonStart.Text = "Observador"
  $buttonStart.Left = 24
  $buttonStart.Top = 270
  $buttonStart.Width = 120
  $buttonStart.Height = 34
  $form.Controls.Add($buttonStart)

  $buttonAutopilot = New-Object System.Windows.Forms.Button
  $buttonAutopilot.Text = "Autopiloto"
  $buttonAutopilot.Left = 156
  $buttonAutopilot.Top = 270
  $buttonAutopilot.Width = 120
  $buttonAutopilot.Height = 34
  $form.Controls.Add($buttonAutopilot)

  $buttonStop = New-Object System.Windows.Forms.Button
  $buttonStop.Text = "Detener"
  $buttonStop.Left = 288
  $buttonStop.Top = 270
  $buttonStop.Width = 92
  $buttonStop.Height = 34
  $form.Controls.Add($buttonStop)

  $buttonOnce = New-Object System.Windows.Forms.Button
  $buttonOnce.Text = "Leer una vez"
  $buttonOnce.Left = 392
  $buttonOnce.Top = 270
  $buttonOnce.Width = 112
  $buttonOnce.Height = 34
  $form.Controls.Add($buttonOnce)

  $buttonPanel = New-Object System.Windows.Forms.Button
  $buttonPanel.Text = "Abrir cabina"
  $buttonPanel.Left = 516
  $buttonPanel.Top = 270
  $buttonPanel.Width = 110
  $buttonPanel.Height = 34
  $form.Controls.Add($buttonPanel)

  $buttonLogs = New-Object System.Windows.Forms.Button
  $buttonLogs.Text = "Logs"
  $buttonLogs.Left = 432
  $buttonLogs.Top = 318
  $buttonLogs.Width = 80
  $buttonLogs.Height = 32
  $form.Controls.Add($buttonLogs)

  $buttonLocal = New-Object System.Windows.Forms.Button
  $buttonLocal.Text = "Panel local"
  $buttonLocal.Left = 296
  $buttonLocal.Top = 318
  $buttonLocal.Width = 100
  $buttonLocal.Height = 32
  $form.Controls.Add($buttonLocal)

  $buttonIntent = New-Object System.Windows.Forms.Button
  $buttonIntent.Text = "Atender alerta"
  $buttonIntent.Left = 160
  $buttonIntent.Top = 318
  $buttonIntent.Width = 124
  $buttonIntent.Height = 32
  $form.Controls.Add($buttonIntent)

  $buttonLearn = New-Object System.Windows.Forms.Button
  $buttonLearn.Text = "Aprender chats"
  $buttonLearn.Left = 24
  $buttonLearn.Top = 318
  $buttonLearn.Width = 124
  $buttonLearn.Height = 32
  $form.Controls.Add($buttonLearn)

  $hint = New-Object System.Windows.Forms.Label
  $hint.Text = "Nivel actual: observa y abre lecturas. No escribe ni envia mensajes al cliente."
  $hint.AutoSize = $true
  $hint.Left = 24
  $hint.Top = 370
  $form.Controls.Add($hint)

  function Refresh-Ui {
    try {
      $status = Get-AgentStatus
      $lines = @(
        "Observador: " + $(if ($status.Running) { "ACTIVO (PID $($status.ProcessId))" } else { "DETENIDO" }),
        "Autopiloto: " + $(if ($status.AutopilotRunning) { "ACTIVO (PID $($status.AutopilotProcessId))" } else { "DETENIDO" }),
        "Config: $($status.ConfigMessage)",
        "Nube: $($status.CloudUrl)",
        "Ultima captura: $($status.LatestCapture)",
        "Ultimo envio procesado: $($status.LatestProcessed)",
        "Ultimo autopiloto: ciclo $($status.LatestAutopilotCycle) $($status.LatestAutopilotStatus) | lineas $($status.LatestAutopilotLines) | alerta $($status.LatestAutopilotIntent) | aprendizaje $($status.LatestAutopilotLearning)",
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
    } catch {
      $statusBox.Text = "No pude leer estado: $($_.Exception.Message)"
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
  "OpenPanel" { Open-AgentPanel }
  "OpenLocalPanel" { Open-LocalPanel }
  "OpenRuntime" { Open-RuntimeFolder }
}
