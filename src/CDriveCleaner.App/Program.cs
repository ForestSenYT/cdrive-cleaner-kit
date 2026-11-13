namespace CDriveCleaner.App;

internal static class Program
{
    /// <summary>
    /// Starts the window. <c>--rules &lt;file&gt;</c> picks another rule file; by default the rule set shipped
    /// next to the executable is used.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (!Environment.Is64BitProcess)
        {
            MessageBox.Show("本程序必须以 64 位进程运行。", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        string rulesPath = Path.Combine(AppContext.BaseDirectory, "rules", "rules.json");
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (args[index] == "--rules")
            {
                rulesPath = Path.GetFullPath(args[index + 1]);
            }
        }

        Application.Run(new MainForm(rulesPath));
        return 0;
    }
}
