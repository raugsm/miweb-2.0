using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
    private readonly CheckBox _rememberCredentialsBox = new();
    private readonly CheckBox _showPasswordBox = new();
    private readonly Label _versionBadge = new();
    private readonly Label _versionLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _assistantDetailLabel = new();
    private readonly TableLayoutPanel _dashboardRoot = new();
    private readonly TableLayoutPanel _cabinSetupPanel = new();
    private readonly Label _cabinSetupTitleLabel = new();
    private readonly Label _cabinSetupDetailLabel = new();
    private readonly Label _cabinSetupChannelsLabel = new();
    private readonly Panel _cabinSetupProgressTrack = new();
    private readonly Panel _cabinSetupProgressFill = new();
    private readonly Label _cabinSetupPercentLabel = new();
    private readonly Label _whatsAppStatusLabel = new();
    private readonly Label _learningStatusLabel = new();
    private readonly Label _accountingStatusLabel = new();
    private readonly Label _safetyStatusLabel = new();
    private readonly Label _handsStatusLabel = new();
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
    private string _lastHealthListSignature = string.Empty;
    private PreflightReport? _cachedPreflight;
    private IReadOnlyList<HealthItem> _cachedHealth = [];
    private DateTimeOffset _lastHeavyRefreshAt = DateTimeOffset.MinValue;
    private bool _cabinSetupActive;
    private int _cabinSetupMaxProgress;
    private string RememberedLoginFile => Path.Combine(_runtime.RuntimeDir, "desktop-login.json");

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
        LoadRememberedLogin();
        _dashboardPanel.Visible = false;
        _loginPanel.Visible = true;
        _loginPanel.BringToFront();
        if (_rememberCredentialsBox.Checked && !string.IsNullOrWhiteSpace(_passwordBox.Text))
        {
            _loginButton.Focus();
        }
        else
        {
            _usernameBox.Focus();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _runtime.IsRunning)
        {
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = true;
            AppendLog("Control Center minimizado: la IA sigue trabajando. Usa Pausar IA para detener motores.");
            return;
        }

        _timer.Stop();
        _runtime.Stop("app_closing");
        _runtime.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        Text = "AriadGSM IA Local";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "ariadgsm-agent.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        Width = 1320;
        Height = 940;
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI Variable Text", 10);
        BackColor = Color.FromArgb(235, 243, 252);
        BuildLoginUi();

        _dashboardRoot.Dock = DockStyle.Fill;
        _dashboardRoot.Padding = new Padding(18);
        _dashboardRoot.RowCount = 6;
        _dashboardRoot.ColumnCount = 1;
        _dashboardRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        _dashboardRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        _dashboardRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _dashboardRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        _dashboardRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        _dashboardRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dashboardPanel.Dock = DockStyle.Fill;
        _dashboardPanel.Visible = false;
        _dashboardPanel.Controls.Add(_dashboardRoot);
        Controls.Add(_dashboardPanel);
        Controls.Add(_loginPanel);
        _loginPanel.BringToFront();

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(18)
        };
        _dashboardRoot.Controls.Add(header, 0, 0);

        var logoPath = Path.Combine(AppContext.BaseDirectory, "assets", "ariadgsm-logo.jpg");
        if (File.Exists(logoPath))
        {
            var logo = new PictureBox
            {
                ImageLocation = logoPath,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Location = new Point(18, 18),
            Size = new Size(230, 76)
            };
            header.Controls.Add(logo);
        }

        var title = new Label
        {
            Text = "AriadGSM IA Local",
            ForeColor = Color.FromArgb(12, 79, 170),
            Font = new Font("Segoe UI Variable Display", 22, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(260, 16)
        };
        var subtitle = new Label
        {
            Text = "Tu asistente local para WhatsApp, aprendizaje del negocio y contabilidad",
            ForeColor = Color.FromArgb(58, 82, 112),
            Font = new Font("Segoe UI Variable Text", 10, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(262, 58)
        };
        _versionBadge.Text = $"Version {_runtime.CurrentVersion}";
        _versionBadge.ForeColor = Color.White;
        _versionBadge.BackColor = Color.FromArgb(24, 120, 242);
        _versionBadge.Font = new Font("Segoe UI Variable Text", 10, FontStyle.Bold);
        _versionBadge.TextAlign = ContentAlignment.MiddleCenter;
        _versionBadge.Location = new Point(1048, 18);
        _versionBadge.Size = new Size(150, 32);
        _versionLabel.Text = $"{_runtime.VersionSummary}";
        _versionLabel.ForeColor = Color.FromArgb(73, 114, 170);
        _versionLabel.Font = new Font("Segoe UI Variable Text", 8, FontStyle.Regular);
        _versionLabel.AutoEllipsis = true;
        _versionLabel.Location = new Point(262, 82);
        _versionLabel.Size = new Size(760, 22);
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
        _dashboardRoot.Controls.Add(buttons, 0, 1);

        ConfigureButton(_startButton, "Encender IA", Color.FromArgb(24, 120, 242));
        ConfigureButton(_stopButton, "Pausar IA", Color.FromArgb(205, 61, 61));
        ConfigureButton(_onceButton, "Leer ahora", Color.FromArgb(17, 145, 101));
        ConfigureButton(_prepareWhatsAppsButton, "Alistar WhatsApps", Color.FromArgb(31, 151, 170));
        ConfigureButton(_panelButton, "Panel web", Color.FromArgb(82, 97, 120));
        ConfigureButton(_logsButton, "Historial", Color.FromArgb(82, 97, 120));
        ConfigureButton(_diagnoseButton, "Reporte", Color.FromArgb(82, 97, 120));
        buttons.Controls.AddRange([
            _startButton,
            _stopButton,
            _onceButton,
            _prepareWhatsAppsButton,
            _panelButton,
            _logsButton,
            _diagnoseButton
        ]);

        _cabinSetupPanel.Dock = DockStyle.Fill;
        _cabinSetupPanel.RowCount = 2;
        _cabinSetupPanel.ColumnCount = 2;
        _cabinSetupPanel.BackColor = Color.White;
        _cabinSetupPanel.Padding = new Padding(16, 10, 16, 10);
        _cabinSetupPanel.Margin = new Padding(0, 0, 0, 8);
        _cabinSetupPanel.Visible = false;
        _cabinSetupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        _cabinSetupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        _cabinSetupPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _cabinSetupPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dashboardRoot.Controls.Add(_cabinSetupPanel, 0, 2);

        _cabinSetupTitleLabel.Text = "Cabina WhatsApp";
        _cabinSetupTitleLabel.Dock = DockStyle.Fill;
        _cabinSetupTitleLabel.Font = new Font("Segoe UI Variable Display", 15, FontStyle.Bold);
        _cabinSetupTitleLabel.ForeColor = Color.FromArgb(12, 79, 170);
        _cabinSetupTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _cabinSetupPanel.Controls.Add(_cabinSetupTitleLabel, 0, 0);

        _cabinSetupProgressTrack.Dock = DockStyle.Fill;
        _cabinSetupProgressTrack.BackColor = Color.FromArgb(224, 235, 249);
        _cabinSetupProgressTrack.Margin = new Padding(0, 8, 0, 6);
        _cabinSetupProgressTrack.Padding = new Padding(0);
        _cabinSetupProgressTrack.Controls.Add(_cabinSetupProgressFill);
        _cabinSetupProgressTrack.Controls.Add(_cabinSetupPercentLabel);
        _cabinSetupProgressTrack.Resize += (_, _) => RenderCabinProgressBar();
        _cabinSetupProgressFill.BackColor = Color.FromArgb(24, 120, 242);
        _cabinSetupProgressFill.Dock = DockStyle.Left;
        _cabinSetupProgressFill.Width = 0;
        _cabinSetupPercentLabel.Dock = DockStyle.Fill;
        _cabinSetupPercentLabel.Text = "0%";
        _cabinSetupPercentLabel.TextAlign = ContentAlignment.MiddleCenter;
        _cabinSetupPercentLabel.ForeColor = Color.FromArgb(12, 79, 170);
        _cabinSetupPercentLabel.Font = new Font("Segoe UI Variable Text", 9, FontStyle.Bold);
        _cabinSetupPercentLabel.BringToFront();
        _cabinSetupPanel.Controls.Add(_cabinSetupProgressTrack, 1, 0);

        _cabinSetupDetailLabel.Text = "Pulsa Alistar WhatsApps para preparar Edge, Chrome y Firefox antes de encender la IA.";
        _cabinSetupDetailLabel.Dock = DockStyle.Fill;
        _cabinSetupDetailLabel.Font = new Font("Segoe UI Variable Text", 10, FontStyle.Regular);
        _cabinSetupDetailLabel.ForeColor = Color.FromArgb(58, 82, 112);
        _cabinSetupDetailLabel.TextAlign = ContentAlignment.TopLeft;
        _cabinSetupPanel.Controls.Add(_cabinSetupDetailLabel, 0, 1);

        _cabinSetupChannelsLabel.Text = "wa-1 Edge: esperando\r\nwa-2 Chrome: esperando\r\nwa-3 Firefox: esperando";
        _cabinSetupChannelsLabel.Dock = DockStyle.Fill;
        _cabinSetupChannelsLabel.Font = new Font("Segoe UI Variable Text", 10, FontStyle.Regular);
        _cabinSetupChannelsLabel.ForeColor = Color.FromArgb(35, 55, 82);
        _cabinSetupChannelsLabel.TextAlign = ContentAlignment.TopLeft;
        _cabinSetupPanel.Controls.Add(_cabinSetupChannelsLabel, 1, 1);

        var problemPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Padding = new Padding(0, 2, 0, 6)
        };
        problemPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        problemPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _dashboardRoot.Controls.Add(problemPanel, 0, 3);

        var assistantPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Color.White,
            Padding = new Padding(18, 14, 18, 12),
            Margin = new Padding(0, 0, 10, 0)
        };
        assistantPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        assistantPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        assistantPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        problemPanel.Controls.Add(assistantPanel, 0, 0);

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.Font = new Font("Segoe UI Variable Display", 16, FontStyle.Bold);
        _summaryLabel.ForeColor = Color.FromArgb(12, 79, 170);
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        assistantPanel.Controls.Add(_summaryLabel, 0, 0);

        _assistantDetailLabel.Dock = DockStyle.Fill;
        _assistantDetailLabel.Font = new Font("Segoe UI Variable Text", 10, FontStyle.Regular);
        _assistantDetailLabel.ForeColor = Color.FromArgb(58, 82, 112);
        _assistantDetailLabel.TextAlign = ContentAlignment.MiddleLeft;
        assistantPanel.Controls.Add(_assistantDetailLabel, 0, 1);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 5,
            BackColor = Color.White
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.Controls.Add(BuildMetricCard("Cabina", _whatsAppStatusLabel, Color.FromArgb(24, 120, 242)), 0, 0);
        cards.Controls.Add(BuildMetricCard("Memoria", _learningStatusLabel, Color.FromArgb(31, 151, 170)), 1, 0);
        cards.Controls.Add(BuildMetricCard("Contabilidad", _accountingStatusLabel, Color.FromArgb(17, 145, 101)), 2, 0);
        cards.Controls.Add(BuildMetricCard("Seguridad", _safetyStatusLabel, Color.FromArgb(67, 91, 142)), 3, 0);
        cards.Controls.Add(BuildMetricCard("Manos", _handsStatusLabel, Color.FromArgb(82, 97, 120)), 4, 0);
        assistantPanel.Controls.Add(cards, 0, 2);

        var needsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.White,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0)
        };
        needsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        needsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        problemPanel.Controls.Add(needsPanel, 1, 0);
        needsPanel.Controls.Add(new Label
        {
            Text = "Lo que necesito de ti",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 44, 72),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _problemBox.Dock = DockStyle.Fill;
        _problemBox.Multiline = true;
        _problemBox.ReadOnly = true;
        _problemBox.ScrollBars = ScrollBars.Vertical;
        _problemBox.BorderStyle = BorderStyle.None;
        _problemBox.BackColor = Color.FromArgb(246, 250, 255);
        _problemBox.ForeColor = Color.FromArgb(45, 69, 102);
        _problemBox.Font = new Font("Segoe UI", 10);
        needsPanel.Controls.Add(_problemBox, 0, 1);

        _healthList.Dock = DockStyle.Fill;
        _healthList.View = View.Details;
        _healthList.FullRowSelect = true;
        _healthList.GridLines = false;
        _healthList.HideSelection = false;
        _healthList.BackColor = Color.FromArgb(252, 254, 255);
        _healthList.Columns.Add("Area de la IA", 230);
        _healthList.Columns.Add("Estado", 110);
        _healthList.Columns.Add("Que significa para operar", 860);
        EnableDoubleBuffering(_healthList);
        _dashboardRoot.Controls.Add(_healthList, 0, 4);

        var activityPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        activityPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        activityPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dashboardRoot.Controls.Add(activityPanel, 0, 5);

        var activityTitle = new Label
        {
            Text = "Lo que la IA hizo",
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
        _activityBox.BorderStyle = BorderStyle.None;
        _activityBox.BackColor = Color.FromArgb(252, 254, 255);
        _activityBox.ForeColor = Color.FromArgb(35, 55, 82);
        _activityBox.Font = new Font("Segoe UI Variable Text", 10);
        activityPanel.Controls.Add(_activityBox, 0, 1);
    }

    private static Panel BuildMetricCard(string title, Label valueLabel, Color accent)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(246, 250, 255),
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(10)
        };
        var stripe = new Panel
        {
            Dock = DockStyle.Left,
            Width = 4,
            BackColor = accent
        };
        var titleLabel = new Label
        {
            Text = title,
            ForeColor = Color.FromArgb(58, 82, 112),
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Location = new Point(16, 8),
            Size = new Size(120, 18)
        };
        valueLabel.ForeColor = Color.FromArgb(16, 32, 55);
        valueLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        valueLabel.Location = new Point(16, 28);
        valueLabel.Size = new Size(128, 48);
        valueLabel.Text = "-";
        panel.Controls.Add(stripe);
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(valueLabel);
        return panel;
    }

    private static void EnableDoubleBuffering(Control control)
    {
        try
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }
        catch
        {
        }
    }

    private void BuildLoginUi()
    {
        _loginPanel.Dock = DockStyle.Fill;
        _loginPanel.BackColor = Color.FromArgb(8, 18, 35);
        _loginPanel.Padding = new Padding(28);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(13, 31, 58)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        _loginPanel.Controls.Add(shell);

        var brandPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(9, 35, 76),
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
                Location = new Point(34, 36),
                Size = new Size(238, 72)
            };
            brandPanel.Controls.Add(logo);
        }

        var brandTitle = new Label
        {
            Text = "AriadGSM\nIA Local",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 25, FontStyle.Bold),
            Location = new Point(34, 155),
            Size = new Size(350, 102)
        };
        var brandCopy = new Label
        {
            Text = "Tu cabina inteligente queda apagada hasta que tu autorices el inicio.",
            ForeColor = Color.FromArgb(188, 216, 250),
            Font = new Font("Segoe UI", 12, FontStyle.Regular),
            Location = new Point(36, 278),
            Size = new Size(330, 82)
        };
        var statusStrip = new Label
        {
            Text = "NUBE SEGURA  |  IA EN PAUSA  |  LISTA PARA TRABAJAR",
            ForeColor = Color.FromArgb(98, 183, 255),
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Location = new Point(36, 390),
            Size = new Size(340, 24)
        };
        var versionCopy = new Label
        {
            Text = _runtime.VersionSummary,
            ForeColor = Color.FromArgb(128, 171, 225),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            Location = new Point(36, 438),
            Size = new Size(340, 48)
        };
        brandPanel.Controls.Add(brandTitle);
        brandPanel.Controls.Add(brandCopy);
        brandPanel.Controls.Add(statusStrip);
        brandPanel.Controls.Add(versionCopy);

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(246, 250, 255),
            Padding = new Padding(58, 62, 58, 48),
            RowCount = 13,
            ColumnCount = 1
        };
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(formPanel, 1, 0);

        var title = new Label
        {
            Text = "Entrar a AriadGSM",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(12, 79, 170),
            Font = new Font("Segoe UI", 24, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitle = new Label
        {
            Text = "Primero entro, reviso actualizaciones y luego tu decides cuando encender la IA.",
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
        _usernameBox.BackColor = Color.White;
        _usernameBox.ForeColor = Color.FromArgb(16, 32, 55);
        _usernameBox.PlaceholderText = "owner, admin o usuario del panel";
        formPanel.Controls.Add(_usernameBox, 0, 3);

        formPanel.Controls.Add(FieldLabel("Contrasena"), 0, 4);
        _passwordBox.Dock = DockStyle.Fill;
        _passwordBox.BorderStyle = BorderStyle.FixedSingle;
        _passwordBox.Font = new Font("Segoe UI", 12);
        _passwordBox.BackColor = Color.White;
        _passwordBox.ForeColor = Color.FromArgb(16, 32, 55);
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.PlaceholderText = "Contrasena de ariadgsm.com";
        formPanel.Controls.Add(_passwordBox, 0, 5);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.FromArgb(246, 250, 255),
            Padding = new Padding(0, 4, 0, 0)
        };
        _rememberCredentialsBox.Text = "Recordar usuario y contrasena en esta PC";
        _rememberCredentialsBox.AutoSize = true;
        _rememberCredentialsBox.ForeColor = Color.FromArgb(45, 69, 102);
        _rememberCredentialsBox.Margin = new Padding(0, 0, 16, 0);
        _showPasswordBox.Text = "Ver";
        _showPasswordBox.AutoSize = true;
        _showPasswordBox.ForeColor = Color.FromArgb(45, 69, 102);
        options.Controls.Add(_rememberCredentialsBox);
        options.Controls.Add(_showPasswordBox);
        formPanel.Controls.Add(options, 0, 6);

        _loginButton.Text = "Entrar a mi cabina";
        _loginButton.Dock = DockStyle.Fill;
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.BackColor = Color.FromArgb(24, 120, 242);
        _loginButton.ForeColor = Color.White;
        _loginButton.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _loginButton.FlatAppearance.BorderSize = 0;
        formPanel.Controls.Add(_loginButton, 0, 8);

        _startupStepLabel.Dock = DockStyle.Fill;
        _startupStepLabel.TextAlign = ContentAlignment.MiddleLeft;
        _startupStepLabel.ForeColor = Color.FromArgb(58, 82, 112);
        _startupStepLabel.Font = new Font("Segoe UI", 9);
        formPanel.Controls.Add(_startupStepLabel, 0, 9);

        _startupProgress.Dock = DockStyle.Fill;
        _startupProgress.Minimum = 0;
        _startupProgress.Maximum = 100;
        _startupProgress.Style = ProgressBarStyle.Continuous;
        formPanel.Controls.Add(_startupProgress, 0, 10);

        _loginStatusLabel.Dock = DockStyle.Fill;
        _loginStatusLabel.ForeColor = Color.FromArgb(82, 97, 120);
        _loginStatusLabel.Font = new Font("Segoe UI", 9);
        _loginStatusLabel.Text = "Sesion requerida para habilitar actualizaciones, cabina e inicio manual.";
        formPanel.Controls.Add(_loginStatusLabel, 0, 11);
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
        _showPasswordBox.CheckedChanged += (_, _) => _passwordBox.UseSystemPasswordChar = !_showPasswordBox.Checked;
        _usernameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _passwordBox.Focus();
            }
        };
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
            if (_runtime.IsRunning)
            {
                var confirmation = MessageBox.Show(
                    this,
                    "Quieres pausar la IA local ahora? Si fue un click accidental, elijo seguir trabajando.",
                    "Confirmar pausa",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    AppendLog("Pausa cancelada: confirmacion no aceptada.");
                    return;
                }
            }

            _runtime.Stop("operator_button");
            RefreshStatus();
        };
        _onceButton.Click += async (_, _) => await RunButtonAsync(_onceButton, () => _runtime.RunOnceAsync()).ConfigureAwait(true);
        _prepareWhatsAppsButton.Click += async (_, _) =>
            await RunButtonAsync(
                _prepareWhatsAppsButton,
                RunCabinSetupAsync).ConfigureAwait(true);
        _panelButton.Click += (_, _) => _runtime.OpenPanel();
        _logsButton.Click += (_, _) => _runtime.OpenLogs();
        _diagnoseButton.Click += (_, _) =>
        {
            var path = _runtime.WriteDiagnosticReport();
            AppendLog($"Diagnostico generado: {path}");
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        };
        _timer.Interval = 2500;
        _timer.Tick += (_, _) =>
        {
            if (_authenticated && _dashboardPanel.Visible)
            {
                RefreshStatus();
            }
        };
    }

    private static void ConfigureButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.Width = 152;
        button.Height = 42;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Variable Text", 9, FontStyle.Bold);
        button.FlatAppearance.BorderSize = 0;
        button.Margin = new Padding(0, 0, 9, 0);
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
            AppendLog("Arranque solicitado: primero alisto la cabina completa; despues enciendo ojos, memoria y manos.");
            report = await _runtime.BootstrapAutonomousWorkspaceAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(true);
            RefreshStatus(report);
        }

        if (report.HasBlockingErrors)
        {
            AppendLog("Arranque bloqueado despues de preparar entorno. Revisa el panel amarillo.");
            BringControlCenterToFront();
            return;
        }

        if (autonomous)
        {
            AppendLog("Cabina preparada. Si algun canal falta, trabajo en modo degradado y lo dejo visible en el panel.");
        }

        await _runtime.StartAsync().ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        RefreshStatus();

        var hasVisibleProblems = HasVisibleProblems();
        if (autonomous && !hasVisibleProblems)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = true;
            return;
        }

        if (!autonomous || hasVisibleProblems)
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
            if (_rememberCredentialsBox.Checked)
            {
                SaveRememberedLogin(_usernameBox.Text.Trim(), _passwordBox.Text);
            }
            else
            {
                ClearRememberedLogin();
            }

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
            if (!_timer.Enabled)
            {
                _timer.Start();
            }

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

    private void LoadRememberedLogin()
    {
        if (!File.Exists(RememberedLoginFile))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(RememberedLoginFile));
            var root = document.RootElement;
            var username = JsonString(root, "username") ?? string.Empty;
            var protectedPassword = JsonString(root, "protectedPassword") ?? string.Empty;
            var password = string.IsNullOrWhiteSpace(protectedPassword)
                ? string.Empty
                : UnprotectSecret(protectedPassword);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            _usernameBox.Text = username;
            _passwordBox.Text = password;
            _rememberCredentialsBox.Checked = true;
            SetLoginProgress(0, "Credenciales recordadas en esta PC. Puedes entrar o cambiarlas.");
        }
        catch
        {
            ClearRememberedLogin();
            SetLoginProgress(0, "No pude leer credenciales guardadas; inicia sesion de nuevo.");
        }
    }

    private void SaveRememberedLogin(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var state = new
        {
            username,
            protectedPassword = ProtectSecret(password),
            protectedBy = "Windows DPAPI CurrentUser",
            updatedAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(RememberedLoginFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ClearRememberedLogin()
    {
        try
        {
            if (File.Exists(RememberedLoginFile))
            {
                File.Delete(RememberedLoginFile);
            }
        }
        catch
        {
        }
    }

    private static string ProtectSecret(string value)
    {
        var payload = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(payload, OptionalEntropy(), DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectSecret(string value)
    {
        var protectedBytes = Convert.FromBase64String(value);
        var payload = ProtectedData.Unprotect(protectedBytes, OptionalEntropy(), DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(payload);
    }

    private static byte[] OptionalEntropy()
    {
        return Encoding.UTF8.GetBytes("AriadGSM.Agent.Desktop.Login.v1");
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

    private async Task RunCabinSetupAsync()
    {
        AppendLog("Alistando WhatsApps: pauso motores, localizo sesiones abiertas, acomodo cabina y valido antes de encender IA.");
        _cabinSetupActive = true;
        _cabinSetupMaxProgress = 0;
        SetCabinSetupVisible(true);
        SetCabinProgressValue(0);
        TopMost = true;
        BringControlCenterToFront();
        _runtime.Stop("prepare_whatsapps");
        UpdateCabinSetupProgress(new CabinSetupProgress(
            5,
            "iniciando",
            "Estoy preparando la cabina. No encendere la IA todavia.",
            [
                new CabinSetupChannelProgress("wa-1", "Edge", "esperando", "Pendiente"),
                new CabinSetupChannelProgress("wa-2", "Chrome", "esperando", "Pendiente"),
                new CabinSetupChannelProgress("wa-3", "Firefox", "esperando", "Pendiente")
            ],
            false,
            false));
        RefreshStatus(null, true);

        try
        {
            var progress = new Progress<CabinSetupProgress>(UpdateCabinSetupProgress);
            var report = await _runtime.PrepareWhatsAppWorkspaceAsync(TimeSpan.FromSeconds(42), progress).ConfigureAwait(true);
            RefreshStatus(report, true);
        }
        finally
        {
            _cabinSetupActive = false;
            BringControlCenterToFront();
            await Task.Delay(120).ConfigureAwait(true);
            TopMost = false;
            BringControlCenterToFront();
        }
    }

    private void RefreshStatus()
    {
        RefreshStatus(null, false);
    }

    private void RefreshStatus(PreflightReport? preflight)
    {
        RefreshStatus(preflight, preflight is not null);
    }

    private void RefreshStatus(PreflightReport? preflight, bool force)
    {
        var now = DateTimeOffset.UtcNow;
        var shouldRefreshHeavy = force
            || preflight is not null
            || _cachedPreflight is null
            || now - _lastHeavyRefreshAt > TimeSpan.FromSeconds(5);

        if (shouldRefreshHeavy)
        {
            _runtime.RefreshControlPlaneSnapshot();
            preflight ??= _runtime.Preflight();
            _cachedPreflight = preflight;
            _cachedHealth = _runtime.Health();
            _lastHeavyRefreshAt = now;
        }
        else
        {
            preflight = _cachedPreflight;
        }

        preflight ??= _cachedPreflight ?? _runtime.Preflight();
        var health = _cachedHealth;
        var allItems = preflight.Items.Concat(health).ToArray();
        var errors = allItems.Where(item => item.Severity == HealthSeverity.Error).ToArray();
        var warnings = allItems.Where(item => item.Severity == HealthSeverity.Warning).ToArray();
        var active = _runtime.ActiveProcessSummary();

        var isRunning = _runtime.IsRunning;
        _summaryLabel.Text = BuildHumanHeadline(isRunning, errors.Length, warnings.Length);
        _assistantDetailLabel.Text = BuildHumanSubtitle(isRunning);
        UpdateMetricCards(preflight);
        if (!_cabinSetupActive && _cabinSetupPanel.Visible)
        {
            UpdateCabinSetupFromState();
        }

        _problemBox.Text = BuildNeedsText(errors, warnings, isRunning);
        UpdateHealthList(allItems);
        _activityBox.Text = BuildHumanActivity(preflight, health, active);
        NotifyOnNewProblem(errors, warnings);
    }

    private void UpdateCabinSetupProgress(CabinSetupProgress progress)
    {
        SetCabinSetupVisible(true);
        SetCabinProgressValue(progress.Percent);
        _cabinSetupTitleLabel.Text = progress.IsReady
            ? "Cabina lista"
            : progress.CanStart
                ? "Cabina parcial"
                : "Alistando cabina";
        _cabinSetupDetailLabel.Text = progress.Summary;
        _cabinSetupChannelsLabel.Text = string.Join(
            Environment.NewLine,
            progress.Channels.Select(channel => $"{channel.ChannelId} {BrowserLabel(channel.Browser)}: {HumanChannelStatus(channel.Status)} - {channel.Detail}"));
        _startButton.Enabled = progress.CanStart || !_cabinSetupActive;
        _cabinSetupPanel.Refresh();
    }

    private void UpdateCabinSetupFromState()
    {
        if (!_cabinSetupPanel.Visible)
        {
            return;
        }

        var workspaceProgress = StateNumber("workspace-setup-state.json", "progress");
        var summary = StateText("cabin-manager-state.json", "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = StateText("status-bus-state.json", "summary");
        }

        if (workspaceProgress > 0)
        {
            SetCabinProgressValue(workspaceProgress);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            _cabinSetupDetailLabel.Text = summary;
        }

        var channels = CabinChannelsFromState();
        if (channels.Length > 0)
        {
            _cabinSetupChannelsLabel.Text = string.Join(
                Environment.NewLine,
                channels.Select(channel => $"{channel.ChannelId} {BrowserLabel(channel.Browser)}: {HumanChannelStatus(channel.Status)} - {channel.Detail}"));
            var ready = channels.Count(channel => channel.Status.Equals("READY", StringComparison.OrdinalIgnoreCase));
            _cabinSetupTitleLabel.Text = ready == channels.Length
                ? "Cabina lista"
                : ready > 0
                    ? "Cabina parcial"
                    : "Cabina pendiente";
        }
    }

    private void SetCabinSetupVisible(bool visible)
    {
        if (_cabinSetupPanel.Visible == visible)
        {
            return;
        }

        _cabinSetupPanel.Visible = visible;
        _dashboardRoot.RowStyles[2].Height = visible ? 176 : 0;
        _dashboardRoot.PerformLayout();
    }

    private void SetCabinProgressValue(int value)
    {
        _cabinSetupMaxProgress = Math.Clamp(Math.Max(_cabinSetupMaxProgress, value), 0, 100);
        _cabinSetupPercentLabel.Text = $"{_cabinSetupMaxProgress}%";
        RenderCabinProgressBar();
    }

    private void RenderCabinProgressBar()
    {
        var width = Math.Max(0, _cabinSetupProgressTrack.ClientSize.Width);
        var fillWidth = (int)Math.Round(width * (_cabinSetupMaxProgress / 100.0));
        _cabinSetupProgressFill.Width = Math.Clamp(fillWidth, 0, width);
        _cabinSetupPercentLabel.BringToFront();
    }

    private CabinSetupChannelProgress[] CabinChannelsFromState()
    {
        var fullPath = Path.Combine(_runtime.RuntimeDir, "cabin-manager-state.json");
        if (!File.Exists(fullPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
            if (!document.RootElement.TryGetProperty("channels", out var channels)
                || channels.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return channels.EnumerateArray()
                .Select(item => new CabinSetupChannelProgress(
                    JsonString(item, "channelId") ?? "-",
                    JsonString(item, "browser") ?? "-",
                    JsonString(item, "status") ?? "-",
                    JsonString(item, "detail") ?? "-"))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string BrowserLabel(string browser)
    {
        return browser.ToLowerInvariant() switch
        {
            "msedge" => "Edge",
            "chrome" => "Chrome",
            "firefox" => "Firefox",
            _ => browser
        };
    }

    private static string HumanChannelStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "READY" => "listo",
            "SEARCHING_EXISTING_SESSION" => "buscando sesion abierta",
            "EXISTING_SESSION_FOUND" => "trayendo al frente",
            "OPENING_MISSING_SESSION" => "abriendo WhatsApp Web",
            "VALIDATING" => "validando",
            "NOT_OPEN" => "no abierto",
            "BROWSER_BUSY_OPEN_WEB" => "navegador ocupado",
            "BROWSER_RUNNING_NO_VISIBLE_WHATSAPP" => "navegador abierto sin WhatsApp visible",
            "LOADING_OR_UNKNOWN" => "cargando",
            "COVERED_BY_WINDOW" => "tapado por otra ventana",
            "NEEDS_USE_HERE" => "necesita Usar aqui",
            "LOGIN_REQUIRED" => "necesita QR/login",
            "PROFILE_ERROR" => "error de perfil",
            "DUPLICATE_WHATSAPP_WINDOWS" => "duplicado",
            "BROWSER_NOT_FOUND" => "navegador no encontrado",
            _ => status.ToLowerInvariant()
        };
    }

    private string BuildHumanHeadline(bool isRunning, int errors, int warnings)
    {
        var kernelHeadline = StateText("runtime-kernel-state.json", "humanReport", "headline");
        if (!string.IsNullOrWhiteSpace(kernelHeadline) && (isRunning || errors > 0 || warnings > 0))
        {
            return kernelHeadline;
        }

        if (errors > 0)
        {
            return "Necesito tu ayuda para seguir";
        }

        if (warnings > 0)
        {
            return isRunning ? "Estoy trabajando con avisos" : "Estoy lista, pero falta revisar algo";
        }

        return isRunning ? "Estoy trabajando contigo" : "Estoy lista para empezar";
    }

    private string BuildHumanSubtitle(bool isRunning)
    {
        var kernelBlocker = StateText("runtime-kernel-state.json", "authority", "mainBlocker");
        if (!string.IsNullOrWhiteSpace(kernelBlocker))
        {
            return kernelBlocker;
        }

        var statusBus = StateText("status-bus-state.json", "summary");
        var statusPhase = StateText("status-bus-state.json", "phase");
        if (!string.IsNullOrWhiteSpace(statusBus) && (_cabinSetupPanel.Visible || !IsCabinSetupPhase(statusPhase)))
        {
            return statusBus;
        }

        var cycle = StateText("autonomous-cycle-state.json", "summary");
        if (!string.IsNullOrWhiteSpace(cycle))
        {
            return cycle;
        }

        return isRunning
            ? "Estoy mirando WhatsApp, guardando memoria y avisando si algo necesita permiso."
            : $"Hola {_operatorName}. Cuando presiones Encender IA preparo WhatsApp, miro, aprendo y sincronizo.";
    }

    private static bool IsCabinSetupPhase(string phase)
    {
        return phase.Equals("observe_channels", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("classify_channels", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("locate_existing_session", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("open_missing_channels", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("validate_channels", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("arrange_windows", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("human_required", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("human_required_degraded", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("partial_ready", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("ready", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("degraded", StringComparison.OrdinalIgnoreCase)
            || phase.Equals("attention", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateMetricCards(PreflightReport preflight)
    {
        var whatsappItems = preflight.Items
            .Where(item => item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var ready = whatsappItems.Count(item => item.Severity == HealthSeverity.Ok);
        var total = Math.Max(3, whatsappItems.Length);
        _whatsAppStatusLabel.Text = $"{ready}/{total} listos{Environment.NewLine}{(ready == total ? "cabina alineada" : "revisando canales")}";

        var memoryMessages = StateNumber("memory-state.json", "summary", "memoryMessages");
        var learningEvents = StateNumber("memory-state.json", "summary", "learningEvents");
        _learningStatusLabel.Text = $"{learningEvents} aprendizajes{Environment.NewLine}{memoryMessages} mensajes";

        var accountingEvents = StateNumber("memory-state.json", "summary", "accountingEvents");
        var accountingDrafts = StateNumber("operating-state.json", "summary", "accountingDrafts");
        _accountingStatusLabel.Text = $"{accountingEvents} registros{Environment.NewLine}{accountingDrafts} borradores";

        var safetyStatus = StateText("trust-safety-state.json", "status");
        var safetyDecision = StateText("trust-safety-state.json", "permissionGate", "decision");
        var blockedActions = StateNumber("trust-safety-state.json", "summary", "blocked");
        _safetyStatusLabel.Text = blockedActions > 0
            ? $"{blockedActions} bloqueos{Environment.NewLine}pide permiso"
            : HumanSafetyMetric(safetyStatus, safetyDecision);

        var actionsExecuted = StateNumber("hands-state.json", "actionsExecuted");
        var actionsVerified = StateNumber("hands-state.json", "actionsVerified");
        var inputPhase = StateText("input-arbiter-state.json", "phase");
        _handsStatusLabel.Text = inputPhase == "operator_control"
            ? $"cedidas a ti{Environment.NewLine}sin pelear mouse"
            : $"{actionsExecuted} acciones{Environment.NewLine}{actionsVerified} verificadas";
    }

    private string BuildNeedsText(IReadOnlyList<HealthItem> errors, IReadOnlyList<HealthItem> warnings, bool isRunning)
    {
        if (errors.Count == 0 && warnings.Count == 0)
        {
            return isRunning
                ? "Nada urgente. Puedes seguir trabajando; yo observo y te aviso si necesito permiso."
                : "Nada urgente. Cuando quieras, pulsa Encender IA para empezar.";
        }

        var lines = new List<string>();
        foreach (var item in errors.Concat(warnings).Take(6))
        {
            lines.Add($"- {HumanizeHealthItem(item)}");
        }

        lines.Add(string.Empty);
        lines.Add("Para detalle completo usa Reporte o Historial.");
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildHumanActivity(
        PreflightReport preflight,
        IReadOnlyList<HealthItem> health,
        IReadOnlyList<string> active)
    {
        var lines = new List<string>();
        var now = DateTimeOffset.Now;
        var kernelHeadline = StateText("runtime-kernel-state.json", "humanReport", "headline");
        var kernelBlocker = StateText("runtime-kernel-state.json", "authority", "mainBlocker");
        if (!string.IsNullOrWhiteSpace(kernelHeadline) || !string.IsNullOrWhiteSpace(kernelBlocker))
        {
            lines.Add($"{now:HH:mm:ss} | Kernel: {kernelHeadline}{(string.IsNullOrWhiteSpace(kernelBlocker) ? string.Empty : $" - {kernelBlocker}")}");
        }

        var phase = StateText("autonomous-cycle-state.json", "phase");
        if (!string.IsNullOrWhiteSpace(phase))
        {
            lines.Add($"{now:HH:mm:ss} | Ahora estoy {HumanPhase(phase)}.");
        }

        var whatsappItems = preflight.Items
            .Where(item => item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var ready = whatsappItems.Count(item => item.Severity == HealthSeverity.Ok);
        lines.Add($"{now:HH:mm:ss} | Cabina: {ready}/{Math.Max(3, whatsappItems.Length)} WhatsApps listos para leer.");
        var cabinSummary = StateText("cabin-manager-state.json", "summary");
        if (_cabinSetupPanel.Visible && !string.IsNullOrWhiteSpace(cabinSummary))
        {
            lines.Add($"{now:HH:mm:ss} | Preparacion: {cabinSummary}");
        }

        var statusBus = StateText("status-bus-state.json", "summary");
        var statusPhase = StateText("status-bus-state.json", "phase");
        if (!string.IsNullOrWhiteSpace(statusBus) && (_cabinSetupPanel.Visible || !IsCabinSetupPhase(statusPhase)))
        {
            lines.Add($"{now:HH:mm:ss} | Estado: {statusBus}");
        }

        lines.Add($"{now:HH:mm:ss} | Memoria: {StateNumber("memory-state.json", "summary", "memoryMessages")} mensajes guardados y {StateNumber("memory-state.json", "summary", "learningEvents")} aprendizajes.");
        lines.Add($"{now:HH:mm:ss} | Contabilidad: {StateNumber("memory-state.json", "summary", "accountingEvents")} eventos detectados.");
        var safetySummary = StateText("trust-safety-state.json", "humanReport", "resumenDecision");
        if (!string.IsNullOrWhiteSpace(safetySummary))
        {
            lines.Add($"{now:HH:mm:ss} | Seguridad: {safetySummary}");
        }

        var inputPhase = StateText("input-arbiter-state.json", "phase");
        var inputSummary = StateText("input-arbiter-state.json", "summary");
        if (!string.IsNullOrWhiteSpace(inputPhase))
        {
            lines.Add($"{now:HH:mm:ss} | Mouse/teclado: {HumanPhase(inputPhase)}. {inputSummary}");
        }
        lines.Add($"{now:HH:mm:ss} | Manos: {StateNumber("hands-state.json", "actionsExecuted")} acciones ejecutadas, {StateNumber("hands-state.json", "actionsVerified")} verificadas.");
        lines.Add($"{now:HH:mm:ss} | Motores: {(active.Count == 0 ? "apagados" : "encendidos")}.");

        var usefulLogs = _recentLogLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(5)
            .ToArray();
        if (usefulLogs.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Ultimos avisos:");
            lines.AddRange(usefulLogs.Select(line => $"- {HumanizeLogLine(line)}"));
        }

        var warnings = health.Where(item => item.Severity == HealthSeverity.Warning).Take(3).ToArray();
        if (warnings.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Estoy vigilando:");
            lines.AddRange(warnings.Select(item => $"- {HumanizeHealthItem(item)}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string HumanSafetyMetric(string status, string decision)
    {
        if (decision.Equals("PAUSE_FOR_OPERATOR", StringComparison.OrdinalIgnoreCase))
        {
            return $"te cedo control{Environment.NewLine}manos pausadas";
        }

        if (decision.Equals("ASK_HUMAN", StringComparison.OrdinalIgnoreCase))
        {
            return $"revisa permiso{Environment.NewLine}antes de actuar";
        }

        if (decision.Equals("ALLOW_WITH_LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            return $"con limites{Environment.NewLine}solo local";
        }

        if (decision.Equals("ALLOW", StringComparison.OrdinalIgnoreCase))
        {
            return $"lista{Environment.NewLine}sin riesgo";
        }

        return status.Equals("ok", StringComparison.OrdinalIgnoreCase)
            ? $"lista{Environment.NewLine}vigilando"
            : $"esperando{Environment.NewLine}sin decision";
    }

    private IEnumerable<string> DiagnosticTimelineLines()
    {
        var path = Path.Combine(_runtime.RuntimeDir, "diagnostic-timeline.jsonl");
        if (!File.Exists(path))
        {
            yield break;
        }

        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path).TakeLast(8).ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string? result = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var created = JsonString(root, "createdAt") ?? string.Empty;
                var source = JsonString(root, "source") ?? "sistema";
                var status = JsonString(root, "status") ?? "-";
                var summary = JsonString(root, "summary") ?? string.Empty;
                var when = DateTimeOffset.TryParse(created, out var parsed)
                    ? parsed.ToLocalTime().ToString("HH:mm:ss")
                    : "--:--:--";
                result = $"{when} | {source} | {status}: {summary}";
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(result))
            {
                yield return result;
            }
        }
    }

    private string HumanizeHealthItem(HealthItem item)
    {
        if (item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase))
        {
            var channel = item.Name.Replace("WhatsApp ", string.Empty, StringComparison.OrdinalIgnoreCase);
            return item.Severity == HealthSeverity.Ok
                ? $"{channel}: listo para leer."
                : $"{channel}: necesito que ese navegador tenga web.whatsapp.com visible o vinculado.";
        }

        return item.Name switch
        {
            "Permisos Windows" => item.Severity == HealthSeverity.Ok
                ? "Windows ya dio permisos suficientes."
                : "Ejecuta la app como administrador para mover ventanas y mouse sin fallar.",
            "Cabina WhatsApp" => $"Cabina WhatsApp: {item.Detail}",
            "Cabin Manager" => $"Cabina: {item.Detail}",
            "Control Plane" => $"Arquitectura 0.6: {item.Detail}",
            "Status Bus" => $"Estado actual: {item.Detail}",
            "Life Controller" => $"Vida de la IA: {item.Detail}",
            "Input Arbiter" => $"Mouse/teclado: {item.Detail}",
            "Action Queue" => $"Cola de acciones: {item.Detail}",
            "Autoridad de cabina" => $"Cabina: {item.Detail}",
            "Ciclo autonomo" => $"Ciclo autonomo: {StateText("autonomous-cycle-state.json", new[] { "summary" }, item.Detail)}",
            "Trust & Safety" => $"Seguridad: {StateText("trust-safety-state.json", new[] { "humanReport", "resumenDecision" }, item.Detail)}",
            "Agent Supervisor" => $"Supervisor interno: {item.Detail}",
            "Accounting Core" => $"Contabilidad: {item.Detail}",
            "Case Manager" => $"Casos: {item.Detail}",
            "Channel Routing" => $"Ruteo de canales: {item.Detail}",
            "Domain Events" => $"Eventos del negocio: {item.Detail}",
            "Interaction" => $"Interaccion: {item.Detail}",
            "Hands" => $"Manos: {item.Detail}",
            "Supervisor" => $"Supervisor: {item.Detail}",
            "Actualizaciones" => $"Actualizaciones: {item.Detail}",
            "WebPanel" => $"Panel web: {item.Detail}",
            _ => $"{item.Name}: {item.Detail}"
        };
    }

    private static string HumanPhase(string phase)
    {
        return phase.ToLowerInvariant() switch
        {
            "acting" => "moviendo manos y verificando",
            "reasoning" => "pensando el siguiente paso",
            "learning" => "aprendiendo del historial",
            "observing" => "observando conversaciones",
            "operator_control" => "te estoy dejando el control",
            "operator_cooldown" => "esperando que sueltes el mouse",
            "ai_control_granted" => "tomando el mouse por un momento",
            "ai_control_released" => "solte el mouse",
            "waiting" => "esperando inicio",
            "blocked" => "bloqueada por seguridad",
            _ => phase
        };
    }

    private static string HumanizeLogLine(string line)
    {
        return line
            .Replace("ERROR", "Error", StringComparison.OrdinalIgnoreCase)
            .Replace("Preflight", "Revision inicial", StringComparison.OrdinalIgnoreCase)
            .Replace("Cabin readiness", "Cabina WhatsApp", StringComparison.OrdinalIgnoreCase)
            .Replace("PythonCoreLoop", "nucleo IA", StringComparison.OrdinalIgnoreCase);
    }

    private int StateNumber(string fileName, params string[] path)
    {
        var value = StateValue(fileName, path);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private string StateText(string fileName, params string[] path)
    {
        return StateText(fileName, path, string.Empty);
    }

    private string StateText(string fileName, string[] path, string fallback)
    {
        var value = StateValue(fileName, path);
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }

        return fallback;
    }

    private JsonElement StateValue(string fileName, params string[] path)
    {
        var fullPath = Path.Combine(_runtime.RuntimeDir, fileName);
        if (!File.Exists(fullPath))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
            var current = document.RootElement;
            foreach (var key in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
                {
                    return default;
                }
            }

            return current.Clone();
        }
        catch
        {
            return default;
        }
    }

    private void UpdateHealthList(IReadOnlyList<HealthItem> items)
    {
        var visibleItems = BuildVisibleHealthItems(items).ToArray();
        var signature = string.Join("|", visibleItems.Select(item => $"{item.Name}:{item.Status}:{item.Severity}:{SummarizeHealthDetail(item)}"));
        if (signature == _lastHealthListSignature)
        {
            return;
        }

        _lastHealthListSignature = signature;
        _healthList.BeginUpdate();
        try
        {
            _healthList.Items.Clear();
            foreach (var item in visibleItems)
            {
                var row = new ListViewItem(item.Name);
                row.SubItems.Add(item.Status);
                row.SubItems.Add(SummarizeHealthDetail(item));
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

    private IEnumerable<HealthItem> BuildVisibleHealthItems(IReadOnlyList<HealthItem> items)
    {
        var allItems = items.ToArray();
        yield return AreaFrom(
            "Cabina WhatsApp",
            allItems.Where(item =>
                item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase)
                || NameIs(item, "Cabina WhatsApp", "Cabin Manager", "Alistamiento cabina", "Autoridad de cabina")),
            "Los 3 canales se estan revisando sin ruido tecnico.");
        yield return AreaFrom(
            "Ojos y lectura",
            allItems.Where(item => NameIs(item, "Vision", "Perception", "Interaction")),
            "Ojos locales listos para leer lo visible.");
        yield return AreaFrom(
            "Cerebro y memoria",
            allItems.Where(item => NameIs(item, "Ciclo autonomo", "Cognitive", "Operating", "Case Manager", "Channel Routing", "Memory", "Domain Events")),
            "La IA puede ordenar conversaciones, casos y memoria.");
        yield return AreaFrom(
            "Contabilidad",
            allItems.Where(item => NameIs(item, "Accounting Core")),
            "Pagos, deudas y borradores contables quedan ligados a evidencia.");
        yield return AreaFrom(
            "Seguridad",
            allItems.Where(item => NameIs(item, "Trust & Safety", "Input Arbiter", "Supervisor", "Agent Supervisor")),
            "Permisos, riesgos y control humano estan vigilados.");
        yield return AreaFrom(
            "Manos",
            allItems.Where(item => NameIs(item, "Hands", "Action Queue")),
            "Mouse y teclado solo actuan si hay permiso y verificacion.");
        yield return AreaFrom(
            "Nube y panel",
            allItems.Where(item => NameIs(item, "Cloud Sync", "Actualizaciones", "WebPanel", "Panel local", "AriadGSM Updater")),
            "Panel, version, actualizaciones y ariadgsm.com quedan sincronizados.");
    }

    private HealthItem AreaFrom(string name, IEnumerable<HealthItem> sourceItems, string okDetail)
    {
        var grouped = sourceItems.Where(item => item is not null).ToArray();
        if (grouped.Length == 0)
        {
            return new HealthItem(name, "ESPERANDO", HealthSeverity.Info, null, okDetail);
        }

        var severity = grouped
            .Select(item => item.Severity)
            .OrderBy(SeverityRank)
            .FirstOrDefault();
        var status = severity switch
        {
            HealthSeverity.Error => "REVISA",
            HealthSeverity.Warning => "ATENCION",
            HealthSeverity.Info => "ESPERANDO",
            HealthSeverity.Ok => "LISTO",
            _ => "ESPERANDO"
        };
        var problems = grouped
            .Where(item => item.Severity == HealthSeverity.Error || item.Severity == HealthSeverity.Warning)
            .OrderBy(item => SeverityRank(item.Severity))
            .ThenBy(item => item.Name)
            .Take(2)
            .Select(HumanizeHealthItem)
            .ToArray();
        var detail = problems.Length > 0
            ? string.Join(" ", problems)
            : okDetail;
        var updatedAt = grouped
            .Where(item => item.UpdatedAt.HasValue)
            .Select(item => item.UpdatedAt)
            .OrderByDescending(item => item)
            .FirstOrDefault();
        return new HealthItem(name, status, severity, updatedAt, detail);
    }

    private static bool NameIs(HealthItem item, params string[] names)
    {
        return names.Any(name => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static int SeverityRank(HealthSeverity severity)
    {
        return severity switch
        {
            HealthSeverity.Error => 0,
            HealthSeverity.Warning => 1,
            HealthSeverity.Info => 2,
            HealthSeverity.Ok => 3,
            _ => 4
        };
    }

    private static string SummarizeHealthDetail(HealthItem item)
    {
        var detail = item.Detail;
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "-";
        }

        if (detail.Contains("Ejecutable listo:", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Carpeta de trabajo lista", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Ejecutando como administrador", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Python listo:", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Node listo:", StringComparison.OrdinalIgnoreCase))
        {
            return "Listo.";
        }

        if (detail.Length <= 140)
        {
            return detail;
        }

        return detail[..137] + "...";
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

    private bool HasBlockingVisibleProblems()
    {
        var report = _runtime.Preflight();
        var health = _runtime.Health();
        return report.Items.Concat(health).Any(item => item.Severity == HealthSeverity.Error);
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
