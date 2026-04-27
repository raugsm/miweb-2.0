using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AriadGSM.Agent.Desktop;

internal sealed class MainForm : Form
{
    private static readonly Uri CloudBaseUri = new("https://ariadgsm.com/");
    private readonly AgentRuntime _runtime = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Panel _loginPanel = new();
    private readonly Panel _dashboardPanel = new();
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly Label _loginStatusLabel = new();
    private readonly Label _startupStepLabel = new();
    private readonly ProgressBar _startupProgress = new();
    private readonly Button _loginButton = new();
    private readonly Label _versionBadge = new();
    private readonly Label _versionLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly TextBox _problemBox = new();
    private readonly ListView _healthList = new();
    private readonly TextBox _activityBox = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _onceButton = new();
    private readonly Button _prepareWhatsAppsButton = new();
    private readonly Button _panelButton = new();
    private readonly Button _logsButton = new();
    private readonly Button _diagnoseButton = new();
    private readonly bool _startMinimized;
    private readonly bool _manualMode;
    private readonly Queue<string> _recentLogLines = new();
    private bool _autonomousStartupStarted;
    private bool _authenticated;
    private string _operatorName = string.Empty;
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
        AppendLog("AriadGSM Agent Desktop listo. Arranque seguro: login primero, motores detenidos.");
        AppendLog("La app no inicia ojos, manos ni IA hasta que entres y presiones Iniciar IA.");
        SetLoginProgress(0, "Ingresa con tu usuario de ariadgsm.com para habilitar la cabina.");
        _dashboardPanel.Visible = false;
        _loginPanel.Visible = true;
        _loginPanel.BringToFront();
        _usernameBox.Focus();

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
        Height = 800;
        MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(247, 251, 255);
        BuildLoginUi();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        _dashboardPanel.Dock = DockStyle.Fill;
        _dashboardPanel.Visible = false;
        _dashboardPanel.Controls.Add(root);
        Controls.Add(_dashboardPanel);
        Controls.Add(_loginPanel);
        _loginPanel.BringToFront();

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(18)
        };
        root.Controls.Add(header, 0, 0);

        var logoPath = Path.Combine(AppContext.BaseDirectory, "assets", "ariadgsm-logo.jpg");
        if (File.Exists(logoPath))
        {
            var logo = new PictureBox
            {
                ImageLocation = logoPath,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Location = new Point(18, 18),
                Size = new Size(220, 72)
            };
            header.Controls.Add(logo);
        }

        var title = new Label
        {
            Text = "AriadGSM Agent Desktop",
            ForeColor = Color.FromArgb(12, 79, 170),
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(260, 16)
        };
        var subtitle = new Label
        {
            Text = "Cabina local para lectura, aprendizaje, manos seguras y sincronizacion con ariadgsm.com",
            ForeColor = Color.FromArgb(58, 82, 112),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(262, 58)
        };
        _versionBadge.Text = $"Version {_runtime.CurrentVersion}";
        _versionBadge.ForeColor = Color.White;
        _versionBadge.BackColor = Color.FromArgb(24, 120, 242);
        _versionBadge.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _versionBadge.TextAlign = ContentAlignment.MiddleCenter;
        _versionBadge.Location = new Point(790, 18);
        _versionBadge.Size = new Size(132, 30);
        _versionLabel.Text = $"{_runtime.VersionSummary} | {AppContext.BaseDirectory}";
        _versionLabel.ForeColor = Color.FromArgb(73, 114, 170);
        _versionLabel.Font = new Font("Segoe UI", 8, FontStyle.Regular);
        _versionLabel.AutoEllipsis = true;
        _versionLabel.Location = new Point(262, 82);
        _versionLabel.Size = new Size(650, 22);
        var headerLine = new Panel
        {
            BackColor = Color.FromArgb(24, 120, 242),
            Dock = DockStyle.Bottom,
            Height = 4
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_versionBadge);
        header.Controls.Add(_versionLabel);
        header.Controls.Add(headerLine);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 12, 0, 0)
        };
        root.Controls.Add(buttons, 0, 1);

        ConfigureButton(_startButton, "Iniciar IA", Color.FromArgb(24, 120, 242));
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

        var activityPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        activityPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        activityPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(activityPanel, 0, 4);

        var activityTitle = new Label
        {
            Text = "Actividad de la IA",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 44, 72),
            TextAlign = ContentAlignment.MiddleLeft
        };
        activityPanel.Controls.Add(activityTitle, 0, 0);

        _activityBox.Dock = DockStyle.Fill;
        _activityBox.Multiline = true;
        _activityBox.ReadOnly = true;
        _activityBox.ScrollBars = ScrollBars.Vertical;
        _activityBox.BorderStyle = BorderStyle.FixedSingle;
        _activityBox.BackColor = Color.White;
        _activityBox.Font = new Font("Segoe UI", 10);
        activityPanel.Controls.Add(_activityBox, 0, 1);
    }

    private void BuildLoginUi()
    {
        _loginPanel.Dock = DockStyle.Fill;
        _loginPanel.BackColor = Color.FromArgb(247, 251, 255);
        _loginPanel.Padding = new Padding(24);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        _loginPanel.Controls.Add(shell);

        var brandPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 79, 170),
            Padding = new Padding(32)
        };
        shell.Controls.Add(brandPanel, 0, 0);

        var logoPath = Path.Combine(AppContext.BaseDirectory, "assets", "ariadgsm-logo.jpg");
        if (File.Exists(logoPath))
        {
            var logo = new PictureBox
            {
                ImageLocation = logoPath,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Location = new Point(32, 36),
                Size = new Size(270, 92)
            };
            brandPanel.Controls.Add(logo);
        }

        var brandTitle = new Label
        {
            Text = "Cabina local segura",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 25, FontStyle.Bold),
            Location = new Point(32, 170),
            Size = new Size(360, 48)
        };
        var brandCopy = new Label
        {
            Text = "Login, actualizaciones y arranque manual antes de encender ojos, memoria y manos.",
            ForeColor = Color.FromArgb(218, 235, 255),
            Font = new Font("Segoe UI", 12, FontStyle.Regular),
            Location = new Point(34, 228),
            Size = new Size(340, 72)
        };
        var versionCopy = new Label
        {
            Text = _runtime.VersionSummary,
            ForeColor = Color.FromArgb(190, 222, 255),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            Location = new Point(34, 330),
            Size = new Size(340, 44)
        };
        brandPanel.Controls.Add(brandTitle);
        brandPanel.Controls.Add(brandCopy);
        brandPanel.Controls.Add(versionCopy);

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(54, 64, 54, 44),
            RowCount = 11,
            ColumnCount = 1
        };
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(formPanel, 1, 0);

        var title = new Label
        {
            Text = "Entrar a AriadGSM",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(12, 79, 170),
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitle = new Label
        {
            Text = "La IA queda apagada hasta que el operador autorice el inicio.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(58, 82, 112),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };
        formPanel.Controls.Add(title, 0, 0);
        formPanel.Controls.Add(subtitle, 0, 1);
        formPanel.Controls.Add(FieldLabel("Usuario"), 0, 2);

        _usernameBox.Dock = DockStyle.Fill;
        _usernameBox.BorderStyle = BorderStyle.FixedSingle;
        _usernameBox.Font = new Font("Segoe UI", 12);
        _usernameBox.PlaceholderText = "owner, admin o usuario del panel";
        formPanel.Controls.Add(_usernameBox, 0, 3);

        formPanel.Controls.Add(FieldLabel("Contrasena"), 0, 4);
        _passwordBox.Dock = DockStyle.Fill;
        _passwordBox.BorderStyle = BorderStyle.FixedSingle;
        _passwordBox.Font = new Font("Segoe UI", 12);
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.PlaceholderText = "Contrasena de ariadgsm.com";
        formPanel.Controls.Add(_passwordBox, 0, 5);

        _loginButton.Text = "Entrar y revisar actualizaciones";
        _loginButton.Dock = DockStyle.Fill;
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.BackColor = Color.FromArgb(24, 120, 242);
        _loginButton.ForeColor = Color.White;
        _loginButton.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _loginButton.FlatAppearance.BorderSize = 0;
        formPanel.Controls.Add(_loginButton, 0, 7);

        _startupStepLabel.Dock = DockStyle.Fill;
        _startupStepLabel.TextAlign = ContentAlignment.MiddleLeft;
        _startupStepLabel.ForeColor = Color.FromArgb(58, 82, 112);
        _startupStepLabel.Font = new Font("Segoe UI", 9);
        formPanel.Controls.Add(_startupStepLabel, 0, 8);

        _startupProgress.Dock = DockStyle.Fill;
        _startupProgress.Minimum = 0;
        _startupProgress.Maximum = 100;
        _startupProgress.Style = ProgressBarStyle.Continuous;
        formPanel.Controls.Add(_startupProgress, 0, 9);

        _loginStatusLabel.Dock = DockStyle.Fill;
        _loginStatusLabel.ForeColor = Color.FromArgb(82, 97, 120);
        _loginStatusLabel.Font = new Font("Segoe UI", 9);
        _loginStatusLabel.Text = "Sesion requerida para habilitar actualizaciones, cabina e inicio manual.";
        formPanel.Controls.Add(_loginStatusLabel, 0, 10);
    }

    private static Label FieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(22, 44, 72),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        };
    }

    private void WireEvents()
    {
        _runtime.LogReceived += AppendLog;
        _loginButton.Click += async (_, _) => await LoginAndPrimeAsync().ConfigureAwait(true);
        _passwordBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await LoginAndPrimeAsync().ConfigureAwait(true);
            }
        };
        _startButton.Click += async (_, _) => await RunButtonAsync(_startButton, () => StartAgentCoreAsync(autonomous: true)).ConfigureAwait(true);
        _stopButton.Click += (_, _) =>
        {
            _runtime.Stop();
            RefreshStatus();
        };
        _onceButton.Click += async (_, _) => await RunButtonAsync(_onceButton, () => _runtime.RunOnceAsync()).ConfigureAwait(true);
        _prepareWhatsAppsButton.Click += async (_, _) =>
            await RunButtonAsync(
                _prepareWhatsAppsButton,
                async () =>
                {
                    AppendLog("Preparando cabina WhatsApp 1/2/3 con diagnostico de bloqueos.");
                    await _runtime.PrepareWhatsAppWorkspaceAsync(TimeSpan.FromSeconds(35)).ConfigureAwait(true);
                }).ConfigureAwait(true);
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
        if (!_authenticated)
        {
            MessageBox.Show(this, "Primero inicia sesion para habilitar la cabina.", "AriadGSM Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _loginPanel.Visible = true;
            _dashboardPanel.Visible = false;
            _loginPanel.BringToFront();
            return;
        }

        BringControlCenterToFront();
        var update = await _runtime.CheckForUpdatesAsync().ConfigureAwait(true);
        AppendLog($"Actualizaciones: {update.Detail}");
        if (update.Available && update.AutoApply && _runtime.TryLaunchUpdater(update))
        {
            AppendLog("Actualizacion automatica iniciada. El updater instalara una version aislada, la probara y el launcher la abrira de nuevo.");
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

        if (autonomous)
        {
            AppendLog("Arranque solicitado: preparo WhatsApp 1/2/3 antes de arrancar motores.");
            report = await _runtime.PrepareWhatsAppWorkspaceAsync(TimeSpan.FromSeconds(35)).ConfigureAwait(true);
            RefreshStatus(report);
        }

        if (report.HasBlockingErrors || (autonomous && HasMissingWhatsApps(report)))
        {
            AppendLog("Arranque bloqueado despues de preparar entorno. Revisa el panel amarillo; puede faltar login QR o navegador.");
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

    private async Task LoginAndPrimeAsync()
    {
        if (_authenticated)
        {
            return;
        }

        _loginButton.Enabled = false;
        try
        {
            SetLoginProgress(12, "Validando usuario en ariadgsm.com...");
            var login = await AuthenticateCloudUserAsync(_usernameBox.Text, _passwordBox.Text).ConfigureAwait(true);
            if (!login.Success)
            {
                SetLoginProgress(0, login.Detail);
                _loginStatusLabel.ForeColor = Color.FromArgb(205, 61, 61);
                return;
            }

            _authenticated = true;
            _operatorName = string.IsNullOrWhiteSpace(login.DisplayName) ? login.Username : login.DisplayName;
            WriteSessionState("authenticated", login.Username, login.DisplayName, login.Role, "Login correcto; revisando actualizaciones.");
            AppendLog($"Sesion iniciada: {_operatorName} ({login.Role}).");

            SetLoginProgress(48, "Login correcto. Revisando actualizaciones...");
            var update = await _runtime.CheckForUpdatesAsync().ConfigureAwait(true);
            AppendLog($"Actualizaciones: {update.Detail}");
            if (update.Available && update.AutoApply && _runtime.TryLaunchUpdater(update))
            {
                SetLoginProgress(92, $"Instalando version {update.LatestVersion}. La app se reiniciara sola.");
                WriteSessionState("updating", login.Username, login.DisplayName, login.Role, $"Actualizacion {update.LatestVersion} iniciada.");
                await Task.Delay(350).ConfigureAwait(true);
                Close();
                return;
            }

            SetLoginProgress(100, "Cabina lista. Presiona Iniciar IA cuando quieras encender el agente.");
            WriteSessionState("ready", login.Username, login.DisplayName, login.Role, "Cabina lista; motores detenidos hasta inicio manual.");
            _loginPanel.Visible = false;
            _dashboardPanel.Visible = true;
            _dashboardPanel.BringToFront();
            RefreshStatus();
        }
        catch (Exception exception)
        {
            SetLoginProgress(0, $"No pude completar el login: {exception.Message}");
            _loginStatusLabel.ForeColor = Color.FromArgb(205, 61, 61);
            AppendLog($"ERROR login: {exception.Message}");
        }
        finally
        {
            _loginButton.Enabled = true;
        }
    }

    private async Task<LoginResult> AuthenticateCloudUserAsync(string username, string password)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return LoginResult.Fail("Escribe usuario y contrasena para entrar.");
        }

        using var client = new HttpClient { BaseAddress = CloudBaseUri, Timeout = TimeSpan.FromSeconds(12) };
        var payload = JsonSerializer.Serialize(new { username, password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("api/auth/login", content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return LoginResult.Fail(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Usuario o contrasena incorrectos."
                : $"ariadgsm.com rechazo el login: {(int)response.StatusCode}.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                return LoginResult.Fail(JsonString(root, "message", "error") ?? "Login rechazado por ariadgsm.com.");
            }

            var user = root.TryGetProperty("user", out var userElement) && userElement.ValueKind == JsonValueKind.Object
                ? userElement
                : root;
            return LoginResult.Ok(
                JsonString(user, "username") ?? username,
                JsonString(user, "displayName", "display_name") ?? username,
                JsonString(user, "role") ?? "operator",
                "Login correcto.");
        }
        catch
        {
            return LoginResult.Ok(username, username, "operator", "Login correcto.");
        }
    }

    private void SetLoginProgress(int value, string detail)
    {
        _startupProgress.Value = Math.Clamp(value, _startupProgress.Minimum, _startupProgress.Maximum);
        _startupStepLabel.Text = detail;
        _loginStatusLabel.ForeColor = Color.FromArgb(82, 97, 120);
        _loginStatusLabel.Text = detail;
    }

    private void WriteSessionState(string status, string username, string displayName, string role, string detail)
    {
        var state = new
        {
            status,
            username,
            displayName,
            role,
            detail,
            version = _runtime.CurrentVersion,
            updatedAt = DateTimeOffset.UtcNow
        };
        var path = Path.Combine(_runtime.RuntimeDir, "desktop-session.json");
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? JsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
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

        var isRunning = _runtime.IsRunning;
        _summaryLabel.Text = errors.Length > 0
            ? $"Estado: requiere atencion ({errors.Length} error/es, {warnings.Length} aviso/s)"
            : warnings.Length > 0
                ? $"Estado: {(isRunning ? "trabajando" : "detenido")} con avisos ({warnings.Length})"
                : $"Estado: listo ({(isRunning ? "motores activos" : "detenido")})";

        var problemLines = new List<string>
        {
            isRunning
                ? "Ventana visible: el agente esta trabajando, pero este panel queda como cabina de control."
                : $"Sesion: {_operatorName}. Motores detenidos hasta que presiones Iniciar IA.",
            $"Procesos: {(active.Count == 0 ? "ninguno" : string.Join(", ", active))}",
            $"Version: {_runtime.VersionSummary}",
            $"Ejecutable: {_runtime.ExecutableDirectory}",
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
        _activityBox.Text = string.Join(Environment.NewLine, _runtime.OperationalActivityLines(preflight, health, _recentLogLines.ToArray()));
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

        _recentLogLines.Enqueue(StartsWithTimestamp(line) ? line : $"{DateTimeOffset.Now:HH:mm:ss} {line}");
        while (_recentLogLines.Count > 80)
        {
            _recentLogLines.Dequeue();
        }
    }

    private static bool StartsWithTimestamp(string line)
    {
        return line.Length >= 19
            && char.IsDigit(line[0])
            && char.IsDigit(line[1])
            && char.IsDigit(line[2])
            && char.IsDigit(line[3])
            && line[4] == '-';
    }

    private sealed record LoginResult(bool Success, string Username, string DisplayName, string Role, string Detail)
    {
        public static LoginResult Ok(string username, string displayName, string role, string detail)
        {
            return new LoginResult(true, username, displayName, role, detail);
        }

        public static LoginResult Fail(string detail)
        {
            return new LoginResult(false, string.Empty, string.Empty, string.Empty, detail);
        }
    }
}
