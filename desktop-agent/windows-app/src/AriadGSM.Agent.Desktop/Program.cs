namespace AriadGSM.Agent.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, "Global\\AriadGSM.Agent.Desktop", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "AriadGSM Agent ya esta abierto. Si no ves la ventana, revisa la barra de tareas o cierra el proceso anterior desde el Administrador de tareas.",
                "AriadGSM Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
    }
}
