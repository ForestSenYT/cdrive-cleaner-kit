using System.Security.Principal;
using System.Text;
using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;
using CDriveCleaner.Core.Text;

namespace CDriveCleaner.App;

/// <summary>
/// Scan, choose, confirm, clean. Rules are shown by group with their default selection, risk and note; a clean
/// always works from the latest scan's file lists and is followed by per-rule deleted and failed counts.
/// </summary>
internal sealed class MainForm : Form
{
    private const int ColumnFiles = 1;
    private const int ColumnSize = 2;
    private const int ColumnRisk = 3;
    private const int ColumnNote = 4;
    private const int ColumnStatus = 5;
    private const int MaxListedFailures = 500;

    private readonly Button _scanButton = new() { Text = "扫描", AutoSize = true };
    private readonly Button _cleanButton = new() { Text = "清理所选…", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "取消", AutoSize = true };
    private readonly Label _rulesLabel = new() { AutoSize = true, AutoEllipsis = true };
    private readonly ListView _list = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        ShowGroups = true,
        HideSelection = false,
        ShowItemToolTips = true,
        MultiSelect = false,
        Dock = DockStyle.Fill,
        AccessibleName = "清理项目",
    };
    private readonly TextBox _details = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        AccessibleName = "详细信息",
    };
    private readonly Label _summary = new() { AutoSize = true, AccessibleName = "勾选合计" };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Minimum = 0, Maximum = 100 };

    private readonly string _rulesPath;
    private readonly bool _shippedRules;
    private RuleSet? _rules;
    private ScanReport? _scan;
    private CleanReport? _clean;
    private CancellationTokenSource? _cancellation;
    private bool _closeRequested;

    /// <param name="shippedRules">False when <c>--rules</c> pointed somewhere else; the window then says so.</param>
    public MainForm(string rulesPath, bool shippedRules)
    {
        _rulesPath = rulesPath;
        _shippedRules = shippedRules;
        Text = Texts.AppTitle;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(LogicalToDeviceUnits(1040), LogicalToDeviceUnits(680));
        MinimumSize = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(480));

        _list.Columns.Add("项目", LogicalToDeviceUnits(230));
        _list.Columns.Add("文件数", LogicalToDeviceUnits(80), HorizontalAlignment.Right);
        _list.Columns.Add("大小", LogicalToDeviceUnits(90), HorizontalAlignment.Right);
        _list.Columns.Add("风险", LogicalToDeviceUnits(45));
        _list.Columns.Add("提示", LogicalToDeviceUnits(330));
        _list.Columns.Add("状态", LogicalToDeviceUnits(220));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        toolbar.Controls.AddRange([_scanButton, _cleanButton, _cancelButton]);

        _split.Panel1.Controls.Add(_list);
        _split.Panel2.Controls.Add(_details);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(LogicalToDeviceUnits(8)),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_rulesLabel, 0, 1);
        layout.Controls.Add(_split, 0, 2);
        layout.Controls.Add(_summary, 0, 3);
        _rulesLabel.Margin = new Padding(3, 4, 3, 6);
        _summary.Margin = new Padding(3, 6, 3, 0);

        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { _statusText, _progress });
        _progress.Size = new Size(LogicalToDeviceUnits(220), _progress.Height);

        Controls.Add(layout);
        Controls.Add(status);
        layout.BringToFront();

        _scanButton.Click += async (_, _) => await ScanAsync();
        _cleanButton.Click += async (_, _) => await CleanAsync();
        _cancelButton.Click += (_, _) => RequestCancel();
        _list.ItemCheck += (_, e) =>
        {
            if (_cancellation is not null)
            {
                e.NewValue = e.CurrentValue; // the selection is fixed while an operation runs
            }
        };
        _list.ItemChecked += (_, _) => UpdateSummary();
        _list.SelectedIndexChanged += (_, _) => ShowDetails();

        UpdateButtons();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _split.Panel2MinSize = LogicalToDeviceUnits(80);
        _split.SplitterDistance = Math.Max(LogicalToDeviceUnits(160), _split.Height * 72 / 100);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadRules();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cancellation is not null)
        {
            // Let the running scan or clean stop at the next file, then close.
            e.Cancel = true;
            _closeRequested = true;
            RequestCancel();
        }

        base.OnFormClosing(e);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Rule files are trusted input that ships with the product (SPEC section 10), so the window offers no way to
    /// pick another one; <c>--rules</c> exists for verification against test fixtures.
    /// </summary>
    private void LoadRules()
    {
        string origin = _shippedRules ? "" : "（不是随程序发布的规则文件）";
        try
        {
            _rules = RuleFileLoader.Load(_rulesPath);
        }
        catch (RuleFileException error)
        {
            _rulesLabel.Text = $"规则文件：{_rulesPath}{origin}（已拒绝）";
            SetStatus("规则文件被拒绝，没有做任何扫描。");
            MessageBox.Show(this, $"规则文件被拒绝，没有做任何扫描：{Environment.NewLine}{error.Message}",
                Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateButtons();
            return;
        }

        _rulesLabel.Text = $"规则文件：{_rulesPath}{origin}";
        if (!_shippedRules)
        {
            _rulesLabel.ForeColor = Texts.MediumRiskColor;
        }

        ShowRules();
        string elevation = IsElevated() ? "" : "当前不是管理员身份，部分系统目录读不到，会按空目录处理。";
        SetStatus($"已加载 {_rules.Rules.Count} 条规则，点击“扫描”开始。{elevation}");
        UpdateButtons();
    }

    private void ShowRules()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        var groups = new Dictionary<string, ListViewGroup>(StringComparer.Ordinal);
        foreach (var rule in _rules!.Rules)
        {
            if (!groups.TryGetValue(rule.Group, out var group))
            {
                group = new ListViewGroup(rule.Group, rule.Group) { CollapsedState = ListViewGroupCollapsedState.Expanded };
                groups.Add(rule.Group, group);
                _list.Groups.Add(group);
            }

            var item = new ListViewItem([rule.Name, "", "", Texts.Risk(rule.Risk), rule.Note ?? "", ""], group)
            {
                Name = rule.Id,
                Tag = rule,
                Checked = rule.DefaultSelected,
                ToolTipText = rule.Description ?? rule.Name,
                UseItemStyleForSubItems = false,
            };
            if (rule.Risk == RiskLevel.Medium)
            {
                item.SubItems[ColumnRisk].ForeColor = Texts.MediumRiskColor;
                item.SubItems[ColumnNote].ForeColor = Texts.MediumRiskColor;
            }

            _list.Items.Add(item);
        }

        _list.EndUpdate();
        _details.Clear();
        UpdateSummary();
    }

    private async Task ScanAsync()
    {
        if (_rules is not { } rules)
        {
            return;
        }

        var cancellation = BeginOperation("正在扫描…");
        var progress = new Progress<ScanProgress>(report =>
        {
            if (_cancellation is null)
            {
                return;
            }

            SetStatus($"正在扫描（{report.Completed}/{report.RuleCount}）：{report.Rule.Name}");
            _progress.Value = report.Completed * 100 / report.RuleCount;
        });

        try
        {
            _scan = await Task.Run(() => new Scanner().Scan(rules, progress, cancellation.Token));
            _clean = null;
            ShowScan();
            SetStatus($"扫描完成：可清理 {_scan.TotalFiles:N0} 个文件，共 {ByteSize.Format(_scan.TotalBytes)}。勾选项目后点击“清理所选…”。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("扫描已取消。");
        }
        catch (Exception error)
        {
            SetStatus("扫描出错。");
            MessageBox.Show(this, $"扫描出错：{error.Message}", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private async Task CleanAsync()
    {
        if (_scan is not { } scan || _clean is not null)
        {
            return;
        }

        var chosen = _list.CheckedItems.Cast<ListViewItem>()
            .Select(item => scan.Find(((Rule)item.Tag!).Id)!)
            .ToList();
        if (chosen.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要清理的项目。", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using (var confirm = new ConfirmCleanForm(chosen))
        {
            if (confirm.ShowDialog(this) != DialogResult.OK)
            {
                SetStatus("已取消清理，没有删除任何文件。");
                return;
            }
        }

        var selectedIds = chosen.Select(result => result.Rule.Id).ToHashSet(StringComparer.Ordinal);
        var cancellation = BeginOperation("正在清理…");
        var progress = new Progress<CleanProgress>(report =>
        {
            if (_cancellation is null)
            {
                return;
            }

            SetStatus($"正在清理：{report.Rule.Name}（{report.Processed:N0}/{report.Total:N0}）");
            _progress.Value = report.Total == 0 ? 100 : (int)(report.Processed * 100 / report.Total);
        });

        try
        {
            _clean = await Task.Run(() => new Cleaner().Clean(scan, selectedIds, progress, cancellation.Token));
            ShowClean();
            long failed = _clean.Rules.Sum(rule => rule.Failed.Count);
            string head = _clean.Cancelled ? "清理已中途取消" : "清理完成";
            SetStatus($"{head}：删除 {_clean.TotalDeletedFiles:N0} 个文件，释放 {ByteSize.Format(_clean.TotalDeletedBytes)}；"
                + $"{failed:N0} 个未删除。需要再次清理请先重新扫描。");
        }
        catch (Exception error)
        {
            SetStatus("清理出错。");
            MessageBox.Show(this, $"清理出错：{error.Message}", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private void ShowScan()
    {
        var scan = _scan!;
        _list.BeginUpdate();
        foreach (ListViewItem item in _list.Items)
        {
            var result = scan.Find(item.Name)!;
            item.SubItems[ColumnFiles].Text = result.Files.ToString("N0");
            item.SubItems[ColumnSize].Text = ByteSize.Format(result.Bytes);
            item.SubItems[ColumnStatus].Text = result.Warnings.Count > 0 ? $"{result.Warnings.Count} 条警告，见下方详情" : "";
        }

        foreach (ListViewGroup group in _list.Groups)
        {
            var results = scan.Rules.Where(result => result.Rule.Group == group.Name).ToList();
            group.Header = $"{group.Name}（{results.Sum(r => r.Files):N0} 个文件，{ByteSize.Format(results.Sum(r => r.Bytes))}）";
        }

        _list.EndUpdate();
        UpdateSummary();
        ShowDetails();
    }

    private void ShowClean()
    {
        var clean = _clean!;
        _list.BeginUpdate();
        foreach (ListViewItem item in _list.Items)
        {
            var result = clean.Find(item.Name);
            item.SubItems[ColumnStatus].Text = result is null
                ? (item.Checked && clean.Cancelled ? "已取消，未处理" : "")
                : $"已删除 {result.DeletedFiles:N0} 个（{ByteSize.Format(result.DeletedBytes)}），失败 {result.Failed.Count:N0} 个";
        }

        _list.EndUpdate();
        ShowDetails();
    }

    private void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not Rule rule)
        {
            _details.Text = "选中一个项目可查看说明、警告和清理失败的文件。";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"{rule.Name}（{rule.Id}）");
        text.AppendLine($"分组：{rule.Group}    风险：{Texts.Risk(rule.Risk)}    默认勾选：{(rule.DefaultSelected ? "是" : "否")}");
        if (!string.IsNullOrEmpty(rule.Description))
        {
            text.AppendLine($"说明：{rule.Description}");
        }

        if (!string.IsNullOrEmpty(rule.Note))
        {
            text.AppendLine($"提示：{rule.Note}");
        }

        if (_scan?.Find(rule.Id) is { } scanned)
        {
            text.Append($"扫描：{scanned.Files:N0} 个文件，{ByteSize.Format(scanned.Bytes)}");
            if (scanned.SkippedTooNew > 0)
            {
                text.Append($"；另有 {scanned.SkippedTooNew:N0} 个文件修改不足 {rule.MinAgeHours:0.##} 小时，已跳过");
            }

            text.AppendLine();
            foreach (var warning in scanned.Warnings)
            {
                text.AppendLine($"  警告（{warning.Location}）：{Texts.Warning(warning.Code)}  {warning.Detail}");
            }
        }

        if (_clean?.Find(rule.Id) is { } cleaned)
        {
            text.AppendLine($"清理：已删除 {cleaned.DeletedFiles:N0} 个文件，{ByteSize.Format(cleaned.DeletedBytes)}；失败 {cleaned.Failed.Count:N0} 个");
            foreach (var failure in cleaned.Failed.Take(MaxListedFailures))
            {
                text.AppendLine($"  {Texts.Error(failure.Error)}：{failure.Path}");
            }

            if (cleaned.Failed.Count > MaxListedFailures)
            {
                text.AppendLine($"  ……其余 {cleaned.Failed.Count - MaxListedFailures:N0} 个未列出");
            }
        }

        _details.Text = text.ToString();
    }

    private void UpdateSummary()
    {
        var checkedRules = _list.CheckedItems.Cast<ListViewItem>().Select(item => item.Name).ToList();
        if (_scan is null)
        {
            _summary.Text = $"已勾选 {checkedRules.Count} 项（尚未扫描）";
            return;
        }

        var results = checkedRules.Select(id => _scan.Find(id)!).ToList();
        _summary.Text = $"已勾选 {results.Count} 项：{results.Sum(r => r.Files):N0} 个文件，{ByteSize.Format(results.Sum(r => r.Bytes))}";
    }

    private CancellationTokenSource BeginOperation(string status)
    {
        _cancellation = new CancellationTokenSource();
        _progress.Value = 0;
        _progress.Visible = true;
        SetStatus(status);
        UpdateButtons();
        return _cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        _cancellation = null;
        cancellation.Dispose();
        _progress.Visible = false;
        UpdateButtons();
        if (_closeRequested)
        {
            BeginInvoke(Close);
        }
    }

    private void RequestCancel()
    {
        if (_cancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
            SetStatus("正在取消…（已删除的文件不会恢复）");
        }
    }

    private void UpdateButtons()
    {
        bool busy = _cancellation is not null;
        _scanButton.Enabled = !busy && _rules is not null;
        _cleanButton.Enabled = !busy && _scan is not null && _clean is null;
        _cancelButton.Enabled = busy;
    }

    private void SetStatus(string text) => _statusText.Text = text;
}
