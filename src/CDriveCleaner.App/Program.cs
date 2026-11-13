namespace CDriveCleaner.App;

internal static class Program
{
    /// <summary>
    /// Starts the window with the rule set shipped next to the executable. <c>--rules &lt;file&gt;</c> loads another
    /// rule file instead; it is meant for verification against test fixtures, and the window marks it as such.
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

        string shippedPath = Path.Combine(AppContext.BaseDirectory, "rules", "rules.json");
        string rulesPath = shippedPath;
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (args[index] == "--rules")
            {
                rulesPath = Path.GetFullPath(args[index + 1]);
            }
        }

        bool shippedRules = string.Equals(rulesPath, shippedPath, StringComparison.OrdinalIgnoreCase);
        Application.Run(new MainForm(rulesPath, shippedRules));
        return 0;
    }
}
