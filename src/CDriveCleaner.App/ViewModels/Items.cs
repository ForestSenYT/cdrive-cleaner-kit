using System.IO;
using System.Windows.Media;
using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.History;
using CDriveCleaner.Core.Insights;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;
using CDriveCleaner.Core.Text;

namespace CDriveCleaner.App.ViewModels;

/// <summary>A rule group; its colour is used by the list, the legend and the capacity strip alike.</summary>
public sealed class GroupItem : ObservableObject
{
    private long _bytes;

    public GroupItem(string name, Color color)
    {
        Name = name;
        Color = color;
        Brush = new SolidColorBrush(color);
        Brush.Freeze();
    }

    public string Name { get; }

    public Color Color { get; }

    public SolidColorBrush Brush { get; }

    public List<RuleItem> Rules { get; } = [];

    /// <summary>What the group could free (every rule, ticked or not, minus what the user keeps).</summary>
    public long Bytes
    {
        get => _bytes;
        set
        {
            if (Set(ref _bytes, value))
            {
                Raise(nameof(BytesText));
            }
        }
    }

    public string BytesText => ByteSize.Format(Bytes);

    public long SelectedBytes => Rules.Where(rule => rule.IsChecked).Sum(rule => rule.EffectiveBytes);

    public void Refresh()
    {
        Bytes = Rules.Sum(rule => rule.EffectiveBytes);
        Raise(nameof(SelectedBytes));
    }
}

public sealed class RuleItem : ObservableObject
{
    private readonly Action _changed;
    private bool _isChecked;
    private RuleScanResult? _scan;
    private IReadOnlyList<SourceItem>? _sources;
    private RuleCleanResult? _lastClean;
    private double _barFraction;

    public RuleItem(Rule rule, GroupItem group, Action changed)
    {
        Rule = rule;
        Group = group;
        _changed = changed;
        _isChecked = rule.DefaultSelected;
    }

    public Rule Rule { get; }

    public GroupItem Group { get; }

    public string Name => Rule.Name;

    public string? Note => Rule.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Rule.Note);

    public string? Description => Rule.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Rule.Description);

    public bool IsMedium => Rule.Risk == RiskLevel.Medium;

    public string RiskText => Texts.Risk(Rule.Risk);

    public bool IsRecycleBin => Rule.Kind == RuleKind.RecycleBin;

    /// <summary>Sources the user chose to keep, by <see cref="Source.Key"/>. Survives a rescan.</summary>
    public HashSet<string> KeptKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
            {
                _changed();
            }
        }
    }

    public RuleScanResult? Scan
    {
        get => _scan;
        set
        {
            _scan = value;
            _sources = null;
            RaiseAll();
        }
    }

    public bool IsScanned => _scan is not null;

    /// <summary>Scanned and nothing to free on this computer.</summary>
    public bool IsEmpty => _scan is { Files: 0, Bytes: 0 };

    public long Files => _scan?.Files ?? 0;

    public long Bytes => _scan?.Bytes ?? 0;

    public long KeptFiles => KeptKeys.Count == 0 ? 0 : Sources.Where(source => !source.IsIncluded).Sum(source => source.Source.Files);

    public long KeptBytes => KeptKeys.Count == 0 ? 0 : Sources.Where(source => !source.IsIncluded).Sum(source => source.Source.Bytes);

    public long EffectiveFiles => Files - KeptFiles;

    public long EffectiveBytes => Bytes - KeptBytes;

    public string SizeText => IsScanned && !IsEmpty ? ByteSize.Format(EffectiveBytes) : "";

    public string FilesText
    {
        get
        {
            if (!IsScanned)
            {
                return "";
            }

            if (IsEmpty)
            {
                return IsRecycleBin ? "回收站是空的" : "没有找到";
            }

            string count = IsRecycleBin ? $"{Files:N0} 项" : $"{EffectiveFiles:N0} 个文件";
            return KeptFiles > 0 ? $"{count}，保留 {KeptSourceCount} 处" : count;
        }
    }

    public int KeptSourceCount => KeptKeys.Count == 0 ? 0 : Sources.Count(source => !source.IsIncluded);

    /// <summary>Share of the largest rule, for the thin bar under the size.</summary>
    public double BarFraction
    {
        get => _barFraction;
        set => Set(ref _barFraction, value);
    }

    public RuleCleanResult? LastClean
    {
        get => _lastClean;
        set
        {
            _lastClean = value;
            Raise(nameof(LastClean), nameof(HasLastClean), nameof(LastCleanText), nameof(FailureLines), nameof(StatusText));
        }
    }

    public bool HasLastClean => _lastClean is not null;

    public string LastCleanText => _lastClean is not { } clean ? ""
        : IsRecycleBin
            ? (clean.Failed.Count == 0 ? $"已清空回收站，释放 {ByteSize.Format(clean.DeletedBytes)}。" : "清空回收站失败。")
            : $"删除 {clean.DeletedFiles:N0} 个文件，释放 {ByteSize.Format(clean.DeletedBytes)}。"
              + (clean.KeptFiles > 0 ? $"按你的选择保留 {clean.KeptFiles:N0} 个。" : "")
              + (clean.Failed.Count > 0 ? $"{clean.Failed.Count:N0} 个没有删除。" : "");

    public IReadOnlyList<string> FailureLines => _lastClean is not { } clean ? []
        : clean.Failed.Take(200).Select(failure => $"{Texts.Error(failure.Error)}：{failure.Path}").ToList();

    /// <summary>The one-line status under the rule's name: the last clean, else warnings, else nothing.</summary>
    public string StatusText
    {
        get
        {
            if (_lastClean is { } clean && !IsRecycleBin)
            {
                return clean.Failed.Count > 0
                    ? $"上次清理释放 {ByteSize.Format(clean.DeletedBytes)}，{clean.Failed.Count:N0} 个正在使用或没有权限"
                    : $"上次清理释放 {ByteSize.Format(clean.DeletedBytes)}";
            }

            return _scan is { Warnings.Count: > 0 } scan ? $"{scan.Warnings.Count} 条提醒，见右侧" : "";
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    public string StatsText => _scan is not { } scan ? "还没有扫描。"
        : IsRecycleBin ? $"回收站里有 {scan.Files:N0} 项，共 {ByteSize.Format(scan.Bytes)}。"
        : scan.Files == 0 ? "没有找到可清理的文件。"
        : $"{scan.Files:N0} 个文件，共 {ByteSize.Format(scan.Bytes)}。";

    public string SkippedText => _scan is { SkippedTooNew: > 0 } scan
        ? $"另有 {scan.SkippedTooNew:N0} 个文件在 {Rule.MinAgeHours:0.##} 小时内改动过，可能还在用，这次不动。"
        : "";

    public bool HasSkipped => SkippedText.Length > 0;

    public IReadOnlyList<string> WarningLines => _scan is not { } scan ? []
        : scan.Warnings.Select(warning => $"{warning.Location}：{Texts.Warning(warning.Code)}" +
            (string.IsNullOrEmpty(warning.Detail) ? "" : $"（{warning.Detail}）")).ToList();

    public bool HasWarnings => _scan is { Warnings.Count: > 0 };

    public IReadOnlyList<SourceItem> Sources
    {
        get
        {
            if (_sources is null)
            {
                if (_scan is not { } scan || IsRecycleBin)
                {
                    return [];
                }

                var sources = SourceBreakdown.Compute(scan);
                double largest = sources.Count == 0 ? 1 : Math.Max(1, sources.Max(source => source.Bytes));
                _sources = sources.Select(source => new SourceItem(source, this, source.Bytes / largest)).ToList();
            }

            return _sources;
        }
    }

    public bool HasSources => !IsRecycleBin && _scan is { Files: > 0 };

    /// <summary>The files to leave alone in this rule, for <see cref="CleanExclusions"/>.</summary>
    public IEnumerable<ScannedFile> KeptFileList =>
        KeptKeys.Count == 0 ? [] : Sources.Where(source => !source.IsIncluded).SelectMany(source => source.Source.FileList);

    internal void SourcesChanged()
    {
        Raise(nameof(KeptFiles), nameof(KeptBytes), nameof(EffectiveFiles), nameof(EffectiveBytes), nameof(SizeText),
            nameof(FilesText), nameof(KeptSourceCount));
        _changed();
    }

    private void RaiseAll() => Raise(
        nameof(Scan), nameof(IsScanned), nameof(IsEmpty), nameof(Files), nameof(Bytes), nameof(KeptFiles), nameof(KeptBytes),
        nameof(EffectiveFiles), nameof(EffectiveBytes), nameof(SizeText), nameof(FilesText), nameof(StatsText),
        nameof(SkippedText), nameof(HasSkipped), nameof(WarningLines), nameof(HasWarnings), nameof(Sources),
        nameof(HasSources), nameof(StatusText), nameof(HasStatus), nameof(KeptSourceCount));
}

public sealed class SourceItem : ObservableObject
{
    private readonly RuleItem _rule;

    public SourceItem(Source source, RuleItem rule, double fraction)
    {
        Source = source;
        _rule = rule;
        Fraction = fraction;
        UsefulFiles = source.MaybeUseful.Take(6).Select(file => new UsefulFile(file)).ToList();
    }

    public Source Source { get; }

    public string Label => Source.Label;

    public string? OwnerHint => Source.Owner is null || Source.Label.StartsWith(Source.Owner, StringComparison.Ordinal)
        ? null : Source.Owner;

    public string SizeText => ByteSize.Format(Source.Bytes);

    public string DetailText
    {
        get
        {
            string age = Texts.Age(Source.NewestWriteUtc);
            return age.Length == 0 ? $"{Source.Files:N0} 个文件" : $"{Source.Files:N0} 个文件，最近改动在{age}";
        }
    }

    public double Fraction { get; }

    public bool IsIncluded
    {
        get => !_rule.KeptKeys.Contains(Source.Key);
        set
        {
            bool changed = value ? _rule.KeptKeys.Remove(Source.Key) : _rule.KeptKeys.Add(Source.Key);
            if (changed)
            {
                Raise();
                _rule.SourcesChanged();
            }
        }
    }

    public bool HasMaybeUseful => Source.MaybeUseful.Count > 0;

    public string MaybeUsefulText =>
        $"其中 {Source.MaybeUseful.Count:N0} 个像是文档、图片或压缩包（{ByteSize.Format(Source.MaybeUsefulBytes)}），删之前看一眼";

    public IReadOnlyList<UsefulFile> UsefulFiles { get; }

    /// <summary>The folder to show in Explorer: the source's first folder, or the folder of its first file.</summary>
    public string? FolderToOpen => Source.Directories.FirstOrDefault()
        ?? (Source.FileList.Count > 0 ? Path.GetDirectoryName(Source.FileList[0].Path) : null);

    public string AutomationName => $"清理 {Label}";
}

public sealed class UsefulFile(ScannedFile file)
{
    public string Name { get; } = System.IO.Path.GetFileName(file.Path);

    public string FullPath { get; } = file.Path;

    public string SizeText { get; } = ByteSize.Format(file.Size);
}

public sealed class HistoryItem(HistoryEntry entry)
{
    public string WhenText { get; } = entry.Time.ToLocalTime().ToString("yyyy 年 M 月 d 日 HH:mm");

    public string FreedText { get; } = ByteSize.Format(entry.DeletedBytes);

    public string DetailText { get; } =
        $"删除 {entry.DeletedFiles:N0} 个文件" + (entry.FailedFiles > 0 ? $"，{entry.FailedFiles:N0} 个没有删除" : "")
        + (entry.Cancelled ? "，中途取消" : "");

    public string RulesText { get; } = string.Join("、", entry.Rules.Select(rule => rule.Name));
}
