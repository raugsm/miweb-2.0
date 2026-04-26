using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed class MainForm : Form
{
    private readonly AgentRuntime _runtime = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly TextBox _statusBox = new();
    private readonly TextBox _logBox = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _onceButton = new();
    private readonly Button _panelButton = new();
    private readonly Button _logsButton = new();
    private readonly bool _startMinimized;

    public MainForm(string[] args)
    {
        _startMinimized = args.Any(arg => arg.Equals("--start-minimized", StringComparison.OrdinalIgnoreCase));
        BuildUi();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = true;
        }

        AppendLog("AriadGSM Agent Desktop listo. Ejecutable real, sin PowerShell operativo.");
        RefreshStatus();
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
        Width = 920;
        Height = 640;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(247, 251, 255);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 4,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
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
            Text = "Vision, lectura, IA local, manos y sincronizacion con ariadgsm.com",
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
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        root.Controls.Add(buttons, 0, 1);

        ConfigureButton(_startButton, "Iniciar agente", Color.FromArgb(24, 120, 242));
        ConfigureButton(_stopButton, "Detener", Color.FromArgb(205, 61, 61));
        ConfigureButton(_onceButton, "Leer una vez", Color.FromArgb(17, 145, 101));
        ConfigureButton(_panelButton, "Abrir panel", Color.FromArgb(82, 97, 120));
        ConfigureButton(_logsButton, "Logs", Color.FromArgb(82, 97, 120));
        buttons.Controls.AddRange([_startButton, _stopButton, _onceButton, _panelButton, _logsButton]);

        _statusBox.Dock = DockStyle.Fill;
        _statusBox.Multiline = true;
        _statusBox.ReadOnly = true;
        _statusBox.ScrollBars = ScrollBars.Vertical;
        _statusBox.BorderStyle = BorderStyle.FixedSingle;
        _statusBox.Font = new Font("Consolas", 10);
        root.Controls.Add(_statusBox, 0, 2);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.Font = new Font("Consolas", 9);
        root.Controls.Add(_logBox, 0, 3);
    }

    private void WireEvents()
    {
        _runtime.LogReceived += AppendLog;
        _startButton.Click += async (_, _) => await RunButtonAsync(_startButton, () => _runtime.StartAsync()).ConfigureAwait(true);
        _stopButton.Click += (_, _) =>
        {
            _runtime.Stop();
            RefreshStatus();
        };
        _onceButton.Click += async (_, _) => await RunButtonAsync(_onceButton, () => _runtime.RunOnceAsync()).ConfigureAwait(true);
        _panelButton.Click += (_, _) => _runtime.OpenPanel();
        _logsButton.Click += (_, _) => _runtime.OpenLogs();
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

    private async Task RunButtonAsync(Button button, Func<Task> action)
    {
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
        var snapshot = _runtime.Snapshot();
        var lines = new List<string>
        {
            $"Estado general: {(_runtime.IsRunning ? "TRABAJANDO" : "DETENIDO")}",
            $"Proyecto: {_runtime.RepoRoot}",
            $"Runtime: {_runtime.RuntimeDir}",
            $"Procesos: {(snapshot.Processes.Count == 0 ? "ninguno" : string.Join(", ", snapshot.Processes))}",
            "",
            $"Vision: {StatusOf(snapshot.Vision)}",
            $"Perception: {StatusOf(snapshot.Perception)}",
            $"Timeline: {StatusOf(snapshot.Timeline)}",
            $"Cognitive: {StatusOf(snapshot.Cognitive)}",
            $"Operating: {StatusOf(snapshot.Operating)}",
            $"Memory: {StatusOf(snapshot.Memory)}",
            $"Hands: {StatusOf(snapshot.Hands)}",
            $"Supervisor: {StatusOf(snapshot.Supervisor)}",
            "",
            "Nivel actual: observa, lee y abre/captura segun politicas. No envia mensajes al cliente."
        };
        _statusBox.Text = string.Join(Environment.NewLine, lines);
    }

    private static string StatusOf(JsonDocument? document)
    {
        if (document is null)
        {
            return "sin estado todavia";
        }

        var root = document.RootElement;
        var status = TryString(root, "status", "Status") ?? "ok";
        var updated = TryString(root, "updatedAt", "UpdatedAt", "observedAt", "ObservedAt") ?? "sin hora";
        var extra = TryNestedNumber(root, "ingested", "messages")
            ?? TryNestedNumber(root, "summary", "memoryMessages")
            ?? TryNumber(root, "messagesExtracted", "MessagesExtracted")
            ?? TryNumber(root, "actionsWritten", "ActionsWritten")
            ?? TryNumber(root, "eventsWritten", "EventsWritten");
        return extra is null
            ? $"{status} | {updated}"
            : $"{status} | {updated} | dato={extra}";
    }

    private static string? TryString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static long? TryNumber(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static long? TryNestedNumber(JsonElement root, string objectName, string numberName)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(objectName, out var child)
            && child.ValueKind == JsonValueKind.Object
                ? TryNumber(child, numberName)
                : null;
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
