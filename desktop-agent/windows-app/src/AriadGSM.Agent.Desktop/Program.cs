using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AriadGSM.Agent.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SetCurrentProcessExplicitAppUserModelID("AriadGSM.Agent");

        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTest();
        }

        using var mutex = new Mutex(initiallyOwned: true, "Global\\AriadGSM.Agent.Desktop", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "AriadGSM Agent ya esta abierto. Si no ves la ventana, revisa la barra de tareas o cierra el proceso anterior desde el Administrador de tareas.",
                "AriadGSM Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
        return 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static int RunSelfTest()
    {
        var baseDir = AppContext.BaseDirectory;
        var requiredFiles = new[]
        {
            "AriadGSM Agent.exe",
            "ariadgsm-version.json",
            Path.Combine("engines", "vision", "AriadGSM.Vision.Worker.exe"),
            Path.Combine("engines", "perception", "AriadGSM.Perception.Worker.exe"),
            Path.Combine("engines", "interaction", "AriadGSM.Interaction.Worker.exe"),
            Path.Combine("engines", "orchestrator", "AriadGSM.Orchestrator.Worker.exe"),
            Path.Combine("engines", "hands", "AriadGSM.Hands.Worker.exe"),
            Path.Combine("config", "vision.json"),
            Path.Combine("config", "perception.json"),
            Path.Combine("config", "interaction.json"),
            Path.Combine("config", "orchestrator.json"),
            Path.Combine("config", "hands.json")
        };

        foreach (var relative in requiredFiles)
        {
            if (!File.Exists(Path.Combine(baseDir, relative)))
            {
                return 10;
            }
        }

        return 0;
    }
}
