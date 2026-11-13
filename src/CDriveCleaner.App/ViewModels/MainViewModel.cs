using System.Collections.ObjectModel;
using System.IO;
using System.Security.Principal;
using System.Windows.Media;
using CDriveCleaner.App.Controls;
using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.History;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;
using CDriveCleaner.Core.Text;

namespace CDriveCleaner.App.ViewModels;

public enum Busy
{
    None,
    Scanning,
    Cleaning,
}

public sealed class MainViewModel : ObservableObject
{
    /// <summary>
    /// Category colours: mid-tone so they hold up on both the light and the dark Mica background, distinct in hue,
    /// none of them red (red is kept for the one irreversible action).
    /// </summary>
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x3B, 0x82, 0xC4), // blue
        Color.FromRgb(0x2B, 0xA3, 0x9A), // teal
        Color.FromRgb(0x8A, 0x6F, 0xD1), // violet
        Color.FromRgb(0xD0, 0x8A, 0x2E), // amber
        Color.FromRgb(0xC2, 0x57, 0x7A), // rose
        Color.FromRgb(0x5E, 0x9E, 0x4A), // green
        Color.FromRgb(0x4F, 0x9F, 0xC9), // sky
    ];

    private static readonly Color RecycleBinColor = Color.FromRgb(0x6B, 0x7A, 0x8F);

    private readonly CleanHistory _history;
    private ScanReport? _scan;
    private Busy _busy;
    private string _status = "";
    private double _progress;
    private CapacitySnapshot _capacity = CapacitySnapshot.Empty;
    private RuleItem? _selected;
    private string _driveText = "";
    private string _freeText = "";
    private string _afterText = "";
    private string? _resultText;
    private bool _showHistory;
    private bool _showEmpty;
    private CancellationTokenSource? _cancellation;

    public MainViewModel(string rulesPath, bool shippedRules)
    {
        RulesPath = rulesPath;
        ShippedRules = shippedRules;
        // Runs against a test rule file (--rules) keep their history in a throwaway file, never in the user's.
        _history = new CleanHistory(shippedRules
            ? CleanHistory.DefaultPath
            : Path.Combine(Path.GetTempPath(), $"cdclean-history-{Environment.ProcessId}.json"));
        IsElevated = CheckElevated();
        SystemDrive = RecycleBin.SystemDriveRoot;
        foreach (var entry in _history.Load())
        {
            History.Add(new HistoryItem(entry));
        }
    }

    public string RulesPath { get; }

    public bool ShippedRules { get; }

    public string RulesFileName => Path.GetFileName(RulesPath);

    public bool IsElevated { get; }

    public string SystemDrive { get; }

    public RuleSet? Rules { get; private set; }

    public ObservableCollection<GroupItem> Groups { get; } = [];

    public ObservableCollection<RuleItem> Items { get; } = [];

    public ObservableCollection<HistoryItem> History { get; } = [];

    public RuleItem? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public Busy Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(IsBusy), nameof(IsIdle), nameof(CanClean), nameof(CanScan));
            }
        }
    }

    public bool IsBusy => Busy != Busy.None;

    public bool IsIdle => Busy == Busy.None;

    public bool CanScan => IsIdle && Rules is not null;

    public bool CanClean => IsIdle && _scan is not null && SelectedBytes + SelectedRecycleItems > 0;

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public CapacitySnapshot Capacity
    {
        get => _capacity;
        private set => Set(ref _capacity, value);
    }

    public string DriveText
    {
        get => _driveText;
        private set => Set(ref _driveText, value);
    }

    public string FreeText
    {
        get => _freeText;
        private set => Set(ref _freeText, value);
    }

    public string AfterText
    {
        get => _afterText;
        private set => Set(ref _afterText, value);
    }

    /// <summary>The outcome of the last clean, shown above the list until the next one.</summary>
    public string? ResultText
    {
        get => _resultText;
        private set
        {
            if (Set(ref _resultText, value))
            {
                Raise(nameof(HasResult));
            }
        }
    }

    public bool HasResult => ResultText is not null;

    public bool ShowHistory
    {
        get => _showHistory;
        set => Set(ref _showHistory, value);
    }

    public bool HasHistory => History.Count > 0;

    /// <summary>Also list the rules that found nothing on this computer.</summary>
    public bool ShowEmpty
    {
        get => _showEmpty;
        set => Set(ref _showEmpty, value);
    }

    public int EmptyCount => Items.Count(item => item.IsEmpty);

    public bool HasEmpty => EmptyCount > 0;

    public string EmptyToggleText => $"显示在这台电脑上没有找到文件的 {EmptyCount} 项";

    public string HistorySummary
    {
        get
        {
            long bytes = 0;
            foreach (var entry in _history.Load())
            {
                bytes += entry.DeletedBytes;
            }

            return History.Count == 0 ? "" : $"{History.Count} 次清理，一共释放 {ByteSize.Format(bytes)}";
        }
    }

    /// <summary>Ticked rules that have something to free; empty ones would do nothing.</summary>
    public IEnumerable<RuleItem> Checked => Items.Where(item => item.IsChecked && item.IsScanned && !item.IsEmpty);

    public long SelectedBytes => Checked.Sum(item => item.EffectiveBytes);

    private long SelectedRecycleItems => Checked.Where(item => item.IsRecycleBin).Sum(item => item.Files);

    public string SelectionText
    {
        get
        {
            if (_scan is null)
            {
                return "";
            }

            int count = Checked.Count();
            return count == 0 ? "没有勾选任何项目" : $"已选 {count} 项，可释放 {ByteSize.Format(SelectedBytes)}";
        }
    }

    public string CleanButtonText => SelectedBytes > 0 ? $"清理 {ByteSize.Format(SelectedBytes)}" : "清理所选";

    /// <summary>Loads the rule file; a rejected file leaves the window empty with the reason in the status.</summary>
    public bool LoadRules()
    {
        try
        {
            Rules = RuleFileLoader.Load(RulesPath);
        }
        catch (RuleFileException error)
        {
            Status = $"规则文件被拒绝，没有做任何扫描：{error.Message}";
            return false;
        }

        var groups = new Dictionary<string, GroupItem>(StringComparer.Ordinal);
        foreach (var rule in Rules.Rules)
        {
            if (!groups.TryGetValue(rule.Group, out var group))
            {
                bool recycle = rule.Kind == RuleKind.RecycleBin;
                group = new GroupItem(rule.Group, recycle ? RecycleBinColor : Palette[groups.Count(g => g.Value.Color != RecycleBinColor) % Palette.Length]);
                groups.Add(rule.Group, group);
                Groups.Add(group);
            }

            var item = new RuleItem(rule, group, SelectionChanged);
            group.Rules.Add(item);
        }

        // Group by group, keeping each group's first appearance in the rule file.
        foreach (var group in Groups)
        {
            foreach (var item in group.Rules)
            {
                Items.Add(item);
            }
        }

        Selected = Items.FirstOrDefault();
        Raise(nameof(CanScan));
        RefreshDrive(morph: false);
        return true;
    }

    public async Task ScanAsync(bool morph = false)
    {
        if (Rules is not { } rules || !IsIdle)
        {
            return;
        }

        using var cancellation = Begin(Busy.Scanning, "正在扫描…");
        var progress = new Progress<ScanProgress>(report =>
        {
            Progress = report.Completed * 100.0 / report.RuleCount;
            Status = $"正在扫描（{report.Completed}/{report.RuleCount}）：{report.Rule.Name}";
        });

        try
        {
            var started = DateTime.UtcNow;
            _scan = await Task.Run(() => new Scanner().Scan(rules, progress, cancellation.Token));
            foreach (var item in Items)
            {
                item.Scan = _scan.Find(item.Rule.Id);
            }

            double largest = Math.Max(1, Items.Max(item => item.Bytes));
            foreach (var item in Items)
            {
                item.BarFraction = item.Bytes / largest;
            }

            RefreshDrive(morph);
            Raise(nameof(EmptyCount), nameof(HasEmpty), nameof(EmptyToggleText));
            ReleaseScanGarbage();
            double seconds = (DateTime.UtcNow - started).TotalSeconds;
            Status = $"扫描完成，用时 {seconds:0.0} 秒。可清理 {_scan.TotalFiles:N0} 个文件，共 {ByteSize.Format(_scan.TotalBytes)}。"
                + (IsElevated ? "" : "以普通用户身份运行时，部分系统文件夹读不到，按空的处理。");
        }
        catch (OperationCanceledException)
        {
            Status = "扫描已取消。";
        }
        catch (Exception error)
        {
            Status = $"扫描出错：{error.Message}";
        }
        finally
        {
            End();
        }
    }

    /// <summary>Deletes the files of the ticked rules (minus the kept sources), records the clean, then rescans.</summary>
    public async Task CleanAsync()
    {
        if (_scan is not { } scan || !CanClean)
        {
            return;
        }

        var chosen = Checked.ToList();
        var ids = chosen.Select(item => item.Rule.Id).ToHashSet(StringComparer.Ordinal);
        var exclusions = CleanExclusions.From(chosen.Select(item => (item.Rule.Id, item.KeptFileList)));
        CleanReport report;
        using (var cancellation = Begin(Busy.Cleaning, "正在清理…"))
        {
            var progress = new Progress<CleanProgress>(update =>
            {
                Progress = update.Total == 0 ? 100 : update.Processed * 100.0 / update.Total;
                Status = $"正在清理（{update.Processed:N0}/{update.Total:N0}）：{update.Rule.Name}";
            });

            try
            {
                report = await Task.Run(() => new Cleaner().Clean(scan, ids, progress, cancellation.Token, exclusions));
            }
            catch (Exception error)
            {
                Status = $"清理出错：{error.Message}";
                End();
                return;
            }
        }

        End();
        foreach (var item in Items)
        {
            item.LastClean = report.Find(item.Rule.Id);
        }

        var entry = HistoryEntry.From(report, DateTimeOffset.Now);
        History.Clear();
        foreach (var past in _history.Add(entry))
        {
            History.Add(new HistoryItem(past));
        }

        Raise(nameof(HasHistory), nameof(HistorySummary));
        long failed = report.Rules.Sum(rule => rule.Failed.Count);
        ResultText = (report.Cancelled ? "清理中途取消。" : "清理完成。")
            + $"释放了 {ByteSize.Format(report.TotalDeletedBytes)}，删除 {report.TotalDeletedFiles:N0} 个文件。"
            + (failed > 0 ? $"{failed:N0} 个文件正在使用或没有权限，留在原处。" : "");
        await ScanAsync(morph: true);
        Status = ResultText;
    }

    public void Cancel() => _cancellation?.Cancel();

    private CancellationTokenSource Begin(Busy busy, string status)
    {
        _cancellation = new CancellationTokenSource();
        Busy = busy;
        Progress = 0;
        Status = status;
        return _cancellation;
    }

    private void End()
    {
        _cancellation = null;
        Busy = Busy.None;
        Raise(nameof(CanClean));
    }

    private void SelectionChanged()
    {
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        RefreshDrive(morph: false);
        Raise(nameof(SelectedBytes), nameof(SelectionText), nameof(CanClean), nameof(CleanButtonText));
    }

    private void RefreshDrive(bool morph)
    {
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        try
        {
            var drive = new DriveInfo(SystemDrive);
            double total = drive.TotalSize;
            double free = drive.AvailableFreeSpace;
            double used = total - drive.TotalFreeSpace;
            var segments = Groups.Select(group => new CapacitySegment(group.Color, group.Bytes, group.SelectedBytes)).ToList();
            Capacity = new CapacitySnapshot(total, used, segments, morph);
            DriveText = $"本地磁盘 {SystemDrive.TrimEnd('\\')}　已用 {ByteSize.Format((long)used)}，共 {ByteSize.Format((long)total)}";
            FreeText = $"空闲 {ByteSize.Format((long)free)}";
            long selected = SelectedBytes;
            string after = ByteSize.Format((long)free + selected);
            AfterText = _scan is null || selected == 0 || after == ByteSize.Format((long)free) ? "" : $"清理所选后约 {after}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            DriveText = $"本地磁盘 {SystemDrive}";
            FreeText = "";
            AfterText = "";
        }

        Raise(nameof(SelectionText), nameof(CleanButtonText), nameof(CanClean));
    }

    /// <summary>
    /// A scan builds tens of megabytes of short-lived lookups (one entry per file found). They are all garbage once
    /// the report exists, so collect them now instead of keeping the memory until the next allocation burst.
    /// </summary>
    private static void ReleaseScanGarbage()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
