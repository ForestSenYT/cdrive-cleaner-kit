using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CDriveCleaner.App;

/// <summary>
/// Starts the window with the rule set shipped next to the executable. <c>--rules &lt;file&gt;</c> loads another rule
/// file instead; it exists for verification against test fixtures, and the window marks such a file.
/// <c>--scan</c> starts scanning once the window is up; without it the user starts the scan.
/// </summary>
public partial class App : Application
{
    public const string ScanSwitch = "--scan";

    /// <summary>Where an unexpected error is written before the window says what happened.</summary>
    public static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CDriveCleaner", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Record(args.Exception);
            MessageBox.Show($"程序出错：{args.Exception.Message}{Environment.NewLine}详情已写入 {CrashLogPath}",
                Texts.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            if (MainWindow is not { IsLoaded: true })
            {
                // Failed before the window was up: there is nothing to return to, so do not linger without a window.
                Shutdown(1);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Record(args.ExceptionObject as Exception);

        // A plain window like this one draws just as fast on the CPU, and the result is then visible to every
        // screen-capture method and immune to graphics-driver trouble; the GPU path showed up blank in GDI captures.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        if (!Environment.Is64BitProcess)
        {
            MessageBox.Show("本程序必须以 64 位进程运行。", Texts.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        string rulesPath = ShippedRuleFile.Path;
        for (int index = 0; index + 1 < e.Args.Length; index++)
        {
            if (e.Args[index] == "--rules")
            {
                rulesPath = Path.GetFullPath(e.Args[index + 1]);
            }
        }

        bool shipped = string.Equals(rulesPath, ShippedRuleFile.Path, StringComparison.OrdinalIgnoreCase);
        bool scanAtStart = e.Args.Contains(ScanSwitch, StringComparer.OrdinalIgnoreCase);
        MainWindow = new MainWindow(rulesPath, shipped, scanAtStart);
        MainWindow.Show();
    }

    private static void Record(Exception? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
