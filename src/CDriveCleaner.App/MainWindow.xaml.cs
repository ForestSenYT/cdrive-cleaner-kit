using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CDriveCleaner.App.ViewModels;
using CDriveCleaner.Core.Text;

namespace CDriveCleaner.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private bool _closeRequested;

    /// <param name="scanAtStart">
    /// Scan as soon as the window is up. Only the "scan as administrator" relaunch asks for it, because clicking that
    /// button already was the request to scan; otherwise nothing is scanned until the user starts it.
    /// </param>
    public MainWindow(string rulesPath, bool shippedRules, bool scanAtStart)
    {
        InitializeComponent();
        _model = new MainViewModel(rulesPath, shippedRules);
        DataContext = _model;
        _model.PropertyChanged += ModelChanged;
        var rules = (CollectionViewSource)Resources["RuleView"];
        rules.Filter += (_, e) => e.Accepted = e.Item is RuleItem item && (_model.ShowEmpty || !item.IsEmpty);
        ContentRendered += async (_, _) =>
        {
            // Loading after the first frame keeps the window's appearance quick.
            if (!_model.LoadRules())
            {
                return;
            }

            if (scanAtStart)
            {
                await ScanAndFocusList();
            }
            else
            {
                StartScanButton.Focus();
            }
        };
        // Space on a highlighted row ticks it, as it would on the check box itself. Preview, because the list box
        // handles Space for its own selection before an ordinary KeyDown handler would see it.
        RuleList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && _model.IsIdle && _model.Selected is { } item && e.OriginalSource is ListBoxItem)
            {
                item.IsChecked = !item.IsChecked;
                e.Handled = true;
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ConfirmLayer.IsVisible)
            {
                HideConfirm();
                e.Handled = true;
            }
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.IsBusy)
        {
            // Let the running scan or clean stop at the next file, then close.
            e.Cancel = true;
            _closeRequested = true;
            _model.Cancel();
        }

        base.OnClosing(e);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowEmpty) or nameof(MainViewModel.EmptyCount))
        {
            ((CollectionViewSource)Resources["RuleView"]).View.Refresh();
            if (_model.Selected is not { } selected || (selected.IsEmpty && !_model.ShowEmpty))
            {
                _model.Selected = _model.Items.FirstOrDefault(item => !item.IsEmpty || _model.ShowEmpty);
            }
        }

        if (e.PropertyName == nameof(MainViewModel.IsBusy) && !_model.IsBusy && _closeRequested)
        {
            Dispatcher.BeginInvoke(Close);
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAndFocusList();

    /// <summary>The scan hides the button that started it, so afterwards the focus goes where the choosing happens.</summary>
    private async Task ScanAndFocusList()
    {
        await _model.ScanAsync();
        if (_closeRequested)
        {
            return;
        }

        if (_model.HasScanned)
        {
            RuleList.Focus();
        }
        else
        {
            StartScanButton.Focus();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _model.Cancel();

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var chosen = _model.Checked.ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        long bytes = chosen.Sum(item => item.EffectiveBytes);
        var notes = new List<string>();
        long keptFiles = chosen.Sum(item => item.KeptFiles);
        if (keptFiles > 0)
        {
            notes.Add($"你取消勾选的来源里的 {keptFiles:N0} 个文件（{ByteSize.Format(chosen.Sum(item => item.KeptBytes))}）会保留。");
        }

        var medium = chosen.Where(item => item.IsMedium).ToList();
        if (medium.Count > 0)
        {
            notes.Add($"其中 {medium.Count} 项是中风险：删除后会失去回滚、还原或免下载修复的能力，详见各项的提示。");
        }

        if (chosen.Any(item => item.IsRecycleBin))
        {
            notes.Add("回收站会被清空，里面的内容都无法再还原。");
        }

        ConfirmList.ItemsSource = chosen;
        ConfirmNotes.ItemsSource = notes;
        ConfirmDelete.Content = $"永久删除 {ByteSize.Format(bytes)}";
        Page.IsEnabled = false;
        ConfirmLayer.Visibility = Visibility.Visible;
        ConfirmCancel.Focus();
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        HideConfirm();
        _model.Status = "已取消清理，没有删除任何文件。";
    }

    private async void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        HideConfirm();
        await _model.CleanAsync();
    }

    private void HideConfirm()
    {
        ConfirmLayer.Visibility = Visibility.Collapsed;
        ConfirmList.ItemsSource = null;
        Page.IsEnabled = true;
        CleanButton.Focus();
    }

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = App.ScanSwitch };
        if (!_model.ShippedRules)
        {
            start.Arguments += $" --rules \"{_model.RulesPath}\"";
        }

        try
        {
            Process.Start(start);
            Close();
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            _model.Status = "没有获得管理员权限，继续以普通用户身份运行。";
        }
    }

    /// <summary>Shows a source's folder in Explorer, so the user can look before deciding. Opening never changes anything.</summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string folder && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }
}
