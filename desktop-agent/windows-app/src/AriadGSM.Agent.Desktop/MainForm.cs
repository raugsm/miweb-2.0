using System.Diagnostics;

namespace AriadGSM.Agent.Desktop;

internal sealed class MainForm : Form
{
    private readonly AgentRuntime _runtime = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Label _summaryLabel = new();
    private readonly TextBox _problemBox = new();
    private readonly ListView _healthList = new();
    private readonly TextBox _logBox = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _onceButton = new();
    private readonly Button _prepareWhatsAppsButton = new();
    private readonly Button _panelButton = new();
    private readonly Button _logsButton = new();
    private readonly Button _diagnoseButton = new();
    private readonly bool _startMinimized;
    private readonly bool _manualMode;
    private bool _autonomousStartupStarted;
    private string _lastProblemSignature = string.Empty;

    public MainForm(string[] args)
    {
        _startMinimized = args.Any(arg => arg.Equals("--start-minimized", StringComparison.OrdinalIgnoreCase));
        _manualMode = args.Any(arg =>
            arg.Equals("--manual", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--no-autostart", StringComparison.OrdinalIgnoreCase));
        BuildUi();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AppendLog("AriadGSM Agent Desktop listo. Ejecutable real, sin PowerShell operativo.");
        AppendLog(_manualMode
            ? "Modo manual activo. Usa Iniciar agente si quieres arrancar desde cabina."
            : "Modo autonomo activo. Voy a revisar actualizaciones, WhatsApp 1/2/3 y motores.");
        RefreshStatus();
        if (!_manualMode)
        {
            BeginInvoke(new Action(async () => await AutonomousStartupAsync().ConfigureAwait(true)));
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _runtime.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        Text = "AriadGSM Agent Desktop";
        Width = 980;
        Height = 760;
        MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(247, 251, 255);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        Controls.Add(root);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 79, 170),
            Padding = new Padding(18)
        };
        root.Controls.Add(header, 0, 0);

        var title = new Label
        {
            Text = "AriadGSM Agent Desktop",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 14)
        };
        var subtitle = new Label
        {
            Text = "Arranque autonomo, actualizaciones, salud de motores y sincronizacion con ariadgsm.com",
            ForeColor = Color.FromArgb(226, 240, 255),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(20, 58)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 12, 0, 0)
        };
        root.Controls.Add(buttons, 0, 1);

        ConfigureButton(_startButton, "Iniciar manual", Color.FromArgb(24, 120, 242));
        ConfigureButton(_stopButton, "Detener", Color.FromArgb(205, 61, 61));
        ConfigureButton(_onceButton, "Leer una vez", Color.FromArgb(17, 145, 101));
        ConfigureButton(_prepareWhatsAppsButton, "Preparar WhatsApps", Color.FromArgb(31, 151, 170));
        ConfigureButton(_panelButton, "Abrir panel", Color.FromArgb(82, 97, 120));
        ConfigureButton(_logsButton, "Logs", Color.FromArgb(82, 97, 120));
        ConfigureButton(_diagnoseButton, "Diagnostico", Color.FromArgb(82, 97, 120));
        buttons.Controls.AddRange([
            _startButton,
            _stopButton,
            _onceButton,
            _prepareWhatsAppsButton,
            _panelButton,
            _logsButton,
            _diagnoseButton
        ]);

        var problemPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        problemPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        problemPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(problemPanel, 0, 2);

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _summaryLabel.ForeColor = Color.FromArgb(22, 44, 72);
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        problemPanel.Controls.Add(_summaryLabel, 0, 0);

        _problemBox.Dock = DockStyle.Fill;
        _problemBox.Multiline = true;
        _problemBox.ReadOnly = true;
        _problemBox.ScrollBars = ScrollBars.Vertical;
        _problemBox.BorderStyle = BorderStyle.FixedSingle;
        _problemBox.BackColor = Color.FromArgb(255, 250, 235);
        _problemBox.Font = new Font("Segoe UI", 10);
        problemPanel.Controls.Add(_problemBox, 0, 1);

        _healthList.Dock = DockStyle.Fill;
        _healthList.View = View.Details;
        _healthList.FullRowSelect = true;
        _healthList.GridLines = true;
        _healthList.HideSelection = false;
        _healthList.Columns.Add("Motor / requisito", 180);
        _healthList.Columns.Add("Estado", 110);
        _healthList.Columns.Add("Ultima vez", 170);
        _healthList.Columns.Add("Detalle", 560);
        root.Controls.Add(_healthList, 0, 3);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.Font = new Font("Consolas", 9);
        root.Controls.Add(_logBox, 0, 4);
    }

    private void WireEvents()
    {
        _runtime.LogReceived += AppendLog;
        _startButton.Click += async (_, _) => await RunButtonAsync(_startButton, () => StartAgentCoreAsync(autonomous: false)).ConfigureAwait(true);
        _stopButton.Click += (_, _) =>
        {
            _runtime.Stop();
            RefreshStatus();
        };
        _onceButton.Click += async (_, _) => await RunButtonAsync(_onceButton, () => _runtime.RunOnceAsync()).ConfigureAwait(true);
        _prepareWhatsAppsButton.Click += (_, _) =>
        {
            _runtime.OpenMissingWhatsApps();
            RefreshStatus();
        };
        _panelButton.Click += (_, _) => _runtime.OpenPanel();
        _logsButton.Click += (_, _) => _runtime.OpenLogs();
        _diagnoseButton.Click += (_, _) =>
        {
            var path = _runtime.WriteDiagnosticReport();
            AppendLog($"Diagnostico generado: {path}");
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        };
        _timer.Interval = 1200;
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
    }

    private static void ConfigureButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.Width = 145;
        button.Height = 38;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.Margin = new Padding(0, 0, 10, 0);
    }

    private async Task AutonomousStartupAsync()
    {
        if (_autonomousStartupStarted)
        {
            return;
        }

        _autonomousStartupStarted = true;
        try
        {
            await StartAgentCoreAsync(autonomous: true).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            AppendLog($"ERROR arranque autonomo: {exception.Message}");
            BringControlCenterToFront();
            RefreshStatus();
        }
    }

    private async Task StartAgentCoreAsync(bool autonomous)
    {
        BringControlCenterToFront();
        var update = await _runtime.CheckForUpdatesAsync().ConfigureAwait(true);
        AppendLog($"Actualizaciones: {update.Detail}");
        if (update.Available && update.AutoApply && _runtime.TryLaunchUpdater(update))
        {
            AppendLog("Actualizacion automatica iniciada. El agente se cerrara y el Updater lo abrira de nuevo.");
            Close();
            return;
        }

        var report = _runtime.Preflight();
        RefreshStatus(report);

        if (report.HasBlockingErrors)
        {
            AppendLog("Inicio bloqueado por diagnostico previo. Revisa el panel amarillo.");
            MessageBox.Show(
                this,
                "Hay errores base antes de iniciar. Revisa 'Que paso' y pulsa Diagnostico si quieres dejarme el reporte.",
                "AriadGSM Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (autonomous && HasMissingWhatsApps(report))
        {
            AppendLog("Faltan uno o mas WhatsApp visibles. Intento prepararlos automaticamente.");
            _runtime.OpenMissingWhatsApps();
            await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(true);
            report = _runtime.Preflight();
            RefreshStatus(report);
        }

        if (report.HasBlockingErrors)
        {
            AppendLog("Arranque bloqueado despues de preparar entorno. Revisa el panel amarillo.");
            BringControlCenterToFront();
            return;
        }

        await _runtime.StartAsync().ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        RefreshStatus();

        if (autonomous && _startMinimized && !HasVisibleProblems())
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = true;
            return;
        }

        if (!autonomous || HasVisibleProblems())
        {
            BringControlCenterToFront();
        }
    }

    private async Task RunButtonAsync(Button button, Func<Task> action)
    {
        BringControlCenterToFront();
        button.Enabled = false;
        try
        {
            await action().ConfigureAwait(true);
            RefreshStatus();
        }
        catch (Exception exception)
        {
            AppendLog($"ERROR: {exception.Message}");
            MessageBox.Show(this, exception.Message, "AriadGSM Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private void RefreshStatus()
    {
        RefreshStatus(null);
    }

    private void RefreshStatus(PreflightReport? preflight)
    {
        preflight ??= _runtime.Preflight();
        var health = _runtime.Health();
        var allItems = preflight.Items.Concat(health).ToArray();
        var errors = allItems.Where(item => item.Severity == HealthSeverity.Error).ToArray();
        var warnings = allItems.Where(item => item.Severity == HealthSeverity.Warning).ToArray();
        var active = _runtime.ActiveProcessSummary();

        _summaryLabel.Text = errors.Length > 0
            ? $"Estado: requiere atencion ({errors.Length} error/es, {warnings.Length} aviso/s)"
            : warnings.Length > 0
                ? $"Estado: trabajando con avisos ({warnings.Length})"
                : $"Estado: listo ({(_runtime.IsRunning ? "motores activos" : "detenido")})";

        var problemLines = new List<string>
        {
            _runtime.IsRunning
                ? "Ventana visible: el agente esta trabajando, pero este panel queda como cabina de control."
                : _manualMode
                    ? "Modo manual: puedes iniciar desde cabina; el pensamiento autonomo queda pausado."
                    : "Modo autonomo: reviso updates, WhatsApp 1/2/3, dependencias y motores; luego arranco.",
            $"Procesos: {(active.Count == 0 ? "ninguno" : string.Join(", ", active))}",
            $"Runtime: {_runtime.RuntimeDir}",
            string.Empty
        };

        if (errors.Length == 0 && warnings.Length == 0)
        {
            problemLines.Add("Que paso: sin errores detectados. Listo para observar, aprender y sincronizar.");
        }
        else
        {
            problemLines.Add("Que paso:");
            problemLines.AddRange(errors.Concat(warnings).Take(10).Select(item => $"- {item.Name}: {item.Detail}"));
        }

        var recentProblems = _runtime.RecentProblemLines(5);
        if (recentProblems.Count > 0)
        {
            problemLines.Add(string.Empty);
            problemLines.Add("Ultimas pistas del log:");
            problemLines.AddRange(recentProblems.Select(line => $"- {line}"));
        }

        _problemBox.Text = string.Join(Environment.NewLine, problemLines);
        UpdateHealthList(allItems);
        NotifyOnNewProblem(errors, warnings);
    }

    private void UpdateHealthList(IReadOnlyList<HealthItem> items)
    {
        _healthList.BeginUpdate();
        try
        {
            _healthList.Items.Clear();
            foreach (var item in items)
            {
                var row = new ListViewItem(item.Name);
                row.SubItems.Add(item.Status);
                row.SubItems.Add(item.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
                row.SubItems.Add(item.Detail);
                row.BackColor = item.Severity switch
                {
                    HealthSeverity.Error => Color.FromArgb(255, 229, 229),
                    HealthSeverity.Warning => Color.FromArgb(255, 248, 218),
                    HealthSeverity.Ok => Color.FromArgb(232, 249, 239),
                    _ => Color.FromArgb(240, 245, 252)
                };
                _healthList.Items.Add(row);
            }
        }
        finally
        {
            _healthList.EndUpdate();
        }
    }

    private void NotifyOnNewProblem(IReadOnlyList<HealthItem> errors, IReadOnlyList<HealthItem> warnings)
    {
        var signature = string.Join("|", errors.Concat(warnings).Select(item => $"{item.Name}:{item.Status}:{item.Detail}"));
        if (string.IsNullOrWhiteSpace(signature) || signature == _lastProblemSignature)
        {
            return;
        }

        _lastProblemSignature = signature;
        AppendLog(errors.Count > 0
            ? $"ATENCION: {errors[0].Name} - {errors[0].Detail}"
            : $"AVISO: {warnings[0].Name} - {warnings[0].Detail}");
    }

    private void BringControlCenterToFront()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        ShowInTaskbar = true;
        Show();
        Activate();
    }

    private static bool HasMissingWhatsApps(PreflightReport report)
    {
        return report.Items.Any(item =>
            item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase)
            && item.Severity == HealthSeverity.Warning);
    }

    private bool HasVisibleProblems()
    {
        var report = _runtime.Preflight();
        var health = _runtime.Health();
        return report.Items.Concat(health).Any(item =>
            item.Severity == HealthSeverity.Error
            || item.Severity == HealthSeverity.Warning);
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }

        _logBox.AppendText(line + Environment.NewLine);
    }
}
