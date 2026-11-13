using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.History;
using CDriveCleaner.Core.Insights;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Tests;

public class SourceBreakdownTests
{
    private const string Temp = @"C:\Users\u\AppData\Local\Temp";
    private static readonly DateTime Old = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RuleScanResult Scan(string root, params (string Relative, long Size)[] files)
    {
        var rule = new Rule("t", "临时文件", "g", RuleKind.Files, null, null, RiskLevel.Low, true, 0, []);
        var result = new RuleScanResult(rule);
        foreach (var (relative, size) in files)
        {
            result.FileList.Add(new ScannedFile(root.TrimEnd('\\') + @"\" + relative, size, root, Old));
        }

        return result;
    }

    [Fact]
    public void Files_are_grouped_by_the_folder_directly_under_the_root()
    {
        var sources = SourceBreakdown.Compute(Scan(Temp, (@"a\x.tmp", 10), (@"a\deep\y.tmp", 20), (@"b\z.tmp", 5), ("loose.tmp", 1)));

        Assert.Equal(["a", "b", "直接放在 Temp 里的文件"], sources.Select(source => source.Label));
        Assert.Equal([30L, 5, 1], sources.Select(source => source.Bytes));
        Assert.Equal([Temp + @"\a"], sources[0].Directories);
        Assert.Empty(sources[2].Directories);
    }

    [Fact]
    public void Folders_left_by_the_same_program_form_one_source()
    {
        var sources = SourceBreakdown.Compute(Scan(Temp,
            (@"0acrq5e4.4bv\InstallCleanup.exe", 100), (@"0acrq5e4.4bv\Assets\logo.png", 50),
            (@"5ts5ibbq.gnh\InstallCleanup.exe", 100), (@"other\notes.docx", 7)));

        var installer = sources[0];
        Assert.Equal("Visual Studio 安装程序（2 个文件夹）", installer.Label);
        Assert.Equal("Visual Studio 安装程序", installer.Owner);
        Assert.Equal(250, installer.Bytes);
        Assert.Equal(3, installer.Files);
        Assert.Empty(installer.MaybeUseful); // an installer's pictures are its own payload

        var other = sources[1];
        Assert.Null(other.Owner);
        Assert.Equal([Temp + @"\other\notes.docx"], other.MaybeUseful.Select(file => file.Path));
    }

    [Fact]
    public void Claude_temp_files_are_recognised_but_still_checked_for_personal_files()
    {
        var sources = SourceBreakdown.Compute(Scan(Temp, (@"claude\s\scratchpad\paper.pdf", 9), (@"claude\s\out.log", 1)));

        Assert.Equal("Claude Code 临时文件", sources.Single().Label);
        Assert.Single(sources.Single().MaybeUseful);
    }

    [Fact]
    public void Sources_beyond_the_limit_are_folded_without_losing_files()
    {
        var files = Enumerable.Range(1, SourceBreakdown.MaxSources + 5)
            .Select(index => ($@"dir{index:000}\f.bin", (long)index))
            .ToArray();
        var sources = SourceBreakdown.Compute(Scan(Temp, files));

        Assert.Equal(SourceBreakdown.MaxSources, sources.Count);
        Assert.Equal("其他 6 处", sources[^1].Label);
        Assert.Equal(files.Sum(file => file.Item2), sources.Sum(source => source.Bytes));
        Assert.Equal(files.Length, sources.Sum(source => source.Files));
        Assert.Equal(6, sources[^1].Directories.Count);
    }

    [Fact]
    public void A_drive_root_is_handled()
    {
        var sources = SourceBreakdown.Compute(Scan(@"C:\", (@"Windows.old\a.dll", 3), ("hiberfil.tmp", 2)));

        Assert.Equal(["Windows.old", "直接放在 C:\\ 里的文件"], sources.Select(source => source.Label));
    }
}

public class KeptPlacesTests
{
    private const string Temp = @"C:\Users\u\AppData\Local\Temp";

    private static RuleScanResult Scan(IEnumerable<(string Relative, long Size)> files)
    {
        var rule = new Rule("t", "临时文件", "g", RuleKind.Files, null, null, RiskLevel.Low, true, 0, []);
        var result = new RuleScanResult(rule);
        foreach (var (relative, size) in files)
        {
            result.FileList.Add(new ScannedFile(Temp + @"\" + relative, size, Temp, DateTime.UtcNow));
        }

        return result;
    }

    private static (string, long)[] Folders(int count, int start = 1, long size = 1) =>
        Enumerable.Range(start, count).Select(index => ($@"dir{index:000}\f.bin", size)).ToArray();

    [Fact]
    public void Keeping_the_folded_entry_still_keeps_its_folders_once_they_are_listed_one_by_one()
    {
        var before = SourceBreakdown.Compute(Scan(Folders(SourceBreakdown.MaxSources + 10)));
        var kept = new KeptPlaces();
        kept.Keep(before[^1]); // "其他 11 处"

        // After a clean fewer folders are left, nothing is folded, and each folder is its own entry.
        var afterScan = Scan(Folders(11, start: SourceBreakdown.MaxSources));
        var after = SourceBreakdown.Compute(afterScan);
        Assert.All(after, source => Assert.Equal(true, kept.Covers(source)));
        Assert.All(afterScan.FileList, file => Assert.True(kept.Keeps(file)));
    }

    [Fact]
    public void A_kept_folder_that_gets_folded_stays_kept_and_marks_the_folded_entry_as_partly_kept()
    {
        var first = Scan(Folders(5, size: 100));
        var kept = new KeptPlaces();
        kept.Keep(SourceBreakdown.Compute(first).Single(source => source.Label == "dir005"));

        // Later many bigger folders appear and push dir005 into the folded entry.
        var second = Scan([.. Folders(5, size: 100), .. Folders(SourceBreakdown.MaxSources, start: 10, size: 1000)]);
        var other = SourceBreakdown.Compute(second)[^1];
        Assert.Contains(Temp + @"\dir005", other.Directories);
        Assert.Null(kept.Covers(other));
        Assert.Single(second.FileList, kept.Keeps);
    }

    [Fact]
    public void A_kept_folder_stays_kept_when_it_is_later_recognised_as_a_program()
    {
        var first = Scan([(@"0acrq5e4.4bv\a.dat", 5)]);
        var kept = new KeptPlaces();
        kept.Keep(SourceBreakdown.Compute(first).Single());

        var second = Scan([(@"0acrq5e4.4bv\a.dat", 5), (@"0acrq5e4.4bv\InstallCleanup.exe", 5), (@"5ts5ibbq.gnh\InstallCleanup.exe", 5)]);
        var installer = SourceBreakdown.Compute(second).Single();
        Assert.Equal("Visual Studio 安装程序", installer.Owner);
        Assert.Null(kept.Covers(installer)); // one of its two folders is kept
        Assert.Equal(2, second.FileList.Count(kept.Keeps));
    }

    [Fact]
    public void Loose_files_are_kept_by_their_root_and_released_again()
    {
        var scan = Scan([("loose.tmp", 1), (@"a\x.tmp", 1)]);
        var loose = SourceBreakdown.Compute(scan).Single(source => source.Directories.Count == 0);
        var kept = new KeptPlaces();
        kept.Keep(loose);
        Assert.Equal(["loose.tmp"], scan.FileList.Where(kept.Keeps).Select(file => Path.GetFileName(file.Path)));

        kept.Release(loose);
        Assert.True(kept.IsEmpty);
        Assert.Equal(false, kept.Covers(loose));
    }
}

public class CleanExclusionsTests
{
    [Fact]
    public void Kept_files_are_matched_per_rule_and_ignore_case()
    {
        var file = new ScannedFile(@"C:\T\a.tmp", 1, @"C:\T", DateTime.UtcNow);
        var exclusions = CleanExclusions.From([("r1", [file])]);

        Assert.True(exclusions.Keeps("r1", @"c:\t\A.TMP"));
        Assert.False(exclusions.Keeps("r2", file.Path));
        Assert.False(CleanExclusions.None.Keeps("r1", file.Path));
        Assert.True(CleanExclusions.None.IsEmpty);
    }
}

public sealed class CleanHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cdclean-history-test-" + Guid.NewGuid().ToString("N"));

    private string HistoryPath => Path.Combine(_directory, "history.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static HistoryEntry Entry(int day, long bytes) => new(
        new DateTimeOffset(2026, 11, day, 9, 0, 0, TimeSpan.FromHours(8)),
        false,
        [new HistoryRule("user-temp", "当前用户临时文件", 3, bytes, 1)]);

    [Fact]
    public void Entries_round_trip_newest_first()
    {
        var history = new CleanHistory(HistoryPath);
        history.Add(Entry(1, 100));
        history.Add(Entry(2, 200));

        var loaded = new CleanHistory(HistoryPath).Load();
        Assert.Equal([200L, 100], loaded.Select(entry => entry.DeletedBytes));
        Assert.Equal(3, loaded[0].DeletedFiles);
        Assert.Equal(1, loaded[0].FailedFiles);
        Assert.Equal("当前用户临时文件", loaded[0].Rules[0].Name);
    }

    [Fact]
    public void A_missing_or_damaged_file_reads_as_empty()
    {
        Assert.Empty(new CleanHistory(HistoryPath).Load());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, "{ not json");
        Assert.Empty(new CleanHistory(HistoryPath).Load());
        Assert.Single(new CleanHistory(HistoryPath).Add(Entry(3, 1)));
    }

    [Fact]
    public void Entries_with_nulls_are_skipped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, """
            [null, {"time":"2026-11-01T09:00:00+08:00","cancelled":false,"rules":null},
             {"time":"2026-11-02T09:00:00+08:00","cancelled":false,"rules":[null]},
             {"time":"2026-11-03T09:00:00+08:00","cancelled":false,"rules":[{"id":"a","name":"n","deleted_files":1,"deleted_bytes":7,"failed_files":0}]}]
            """);

        var loaded = new CleanHistory(HistoryPath).Load();
        Assert.Equal(7, Assert.Single(loaded).DeletedBytes);
    }

    [Fact]
    public void A_file_that_cannot_be_read_right_now_is_not_overwritten()
    {
        var history = new CleanHistory(HistoryPath);
        history.Add(Entry(1, 100));
        byte[] before = File.ReadAllBytes(HistoryPath);
        using (new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Single(history.Add(Entry(2, 200))); // the new entry is still shown for this session
        }

        Assert.Equal(before, File.ReadAllBytes(HistoryPath));
    }

    [Fact]
    public void A_damaged_file_is_kept_aside_before_starting_afresh()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, "{ not json");
        new CleanHistory(HistoryPath).Add(Entry(4, 1));
        Assert.Equal("{ not json", File.ReadAllText(HistoryPath + ".damaged"));
        Assert.Single(new CleanHistory(HistoryPath).Load());
    }

    [Fact]
    public void Old_entries_are_dropped_beyond_the_limit()
    {
        var history = new CleanHistory(HistoryPath);
        for (int index = 0; index < CleanHistory.MaxEntries + 3; index++)
        {
            history.Add(Entry(1, index));
        }

        var loaded = history.Load();
        Assert.Equal(CleanHistory.MaxEntries, loaded.Count);
        Assert.Equal(CleanHistory.MaxEntries + 2, loaded[0].DeletedBytes);
    }
}
