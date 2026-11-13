using CDriveCleaner.Core.Paths;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Core.Insights;

/// <summary>
/// One place a rule's files come from: a folder directly under the scanned root, several such folders that the
/// same program left behind, or the loose files lying in the root itself.
/// </summary>
/// <param name="Key">Stable identity inside one rule's breakdown, for remembering what the user chose to keep.</param>
/// <param name="Label">What people see: the program's name when it is recognised, otherwise the folder name.</param>
/// <param name="Owner">The recognised program, or null.</param>
/// <param name="Directories">The folders this source covers; empty for loose files.</param>
/// <param name="FileList">The scanned files of this source: the unit that is kept or cleaned together.</param>
/// <param name="MaybeUseful">Documents, pictures, media, archives and similar personal-looking files, largest first.</param>
public sealed record Source(
    string Key,
    string Label,
    string? Owner,
    IReadOnlyList<string> Directories,
    IReadOnlyList<ScannedFile> FileList,
    long Bytes,
    DateTime NewestWriteUtc,
    IReadOnlyList<ScannedFile> MaybeUseful)
{
    public long Files => FileList.Count;

    public long MaybeUsefulBytes => MaybeUseful.Sum(file => file.Size);
}

/// <summary>
/// Groups a rule's scan list by where the files sit, recognising the folders some common programs leave behind.
/// Works on the scan list only; nothing is read from disk.
/// </summary>
public static class SourceBreakdown
{
    /// <summary>Sources beyond this many (by size) are folded into one "other places" entry.</summary>
    public const int MaxSources = 30;

    private const string LooseKey = "loose:";

    private static readonly HashSet<string> PersonalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".md", ".rtf", ".csv", ".odt", ".ods",
        ".odp", ".wps", ".et", ".dps", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic", ".webp", ".tif", ".tiff",
        ".psd", ".ai", ".svg", ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".mp3", ".wav", ".flac", ".m4a",
        ".zip", ".rar", ".7z", ".tar", ".gz", ".ma", ".mb", ".blend", ".fbx", ".obj", ".max", ".c4d", ".skp",
        ".dwg", ".py", ".ipynb", ".sql", ".epub",
    };

    public static IReadOnlyList<Source> Compute(RuleScanResult result)
    {
        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in result.FileList)
        {
            int start = file.Root.EndsWith(PathText.Separator) ? file.Root.Length : file.Root.Length + 1;
            int end = start < file.Path.Length ? file.Path.IndexOf(PathText.Separator, start) : -1;
            string key = end < 0 ? LooseKey + file.Root : file.Path[..end];
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = end < 0
                    ? new Bucket(key, $"直接放在 {FolderName(file.Root)} 里的文件", null)
                    : new Bucket(key, file.Path[start..end], file.Path[..end]);
                buckets.Add(key, bucket);
            }

            bucket.Add(file);
        }

        // Folders that the same program left behind form one source; every other folder stays on its own.
        var drafts = buckets.Values
            .GroupBy(bucket => bucket.Owner is { } owner ? "owner:" + owner.Name : bucket.Key, StringComparer.Ordinal)
            .Select(group => new Draft(group.Key, group.ToList()))
            .OrderByDescending(draft => draft.Bytes)
            .ToList();

        if (drafts.Count > MaxSources)
        {
            var rest = drafts.Skip(MaxSources - 1).ToList();
            drafts = [.. drafts.Take(MaxSources - 1), new Draft("other", rest.SelectMany(draft => draft.Parts).ToList(), $"其他 {rest.Count} 处")];
        }

        return drafts.Select(draft => draft.Build()).ToList();
    }

    private static string FolderName(string root)
    {
        string trimmed = root.TrimEnd(PathText.Separator);
        int slash = trimmed.LastIndexOf(PathText.Separator);
        return slash < 0 ? trimmed + PathText.Separator : trimmed[(slash + 1)..];
    }

    private sealed class Draft(string key, List<Bucket> parts, string? label = null)
    {
        public List<Bucket> Parts { get; } = parts;

        public long Bytes { get; } = parts.Sum(part => part.Bytes);

        public Source Build()
        {
            // Every part of an owner group shares its owner; the folded "other places" entry mixes several, so it has none.
            var owners = Parts.Select(part => part.Owner).Distinct().ToList();
            var owner = owners.Count == 1 ? owners[0] : null;
            string text = label
                ?? (owner is null ? Parts[0].Label
                    : Parts.Count > 1 ? $"{owner.Name}（{Parts.Count} 个文件夹）" : owner.Name);

            var files = Parts.SelectMany(part => part.Files).ToList();
            var maybeUseful = Parts.Where(part => part.Owner?.MayHoldPersonalFiles ?? true)
                .SelectMany(part => part.Files)
                .Where(file => PersonalExtensions.Contains(Path.GetExtension(file.Path)))
                .OrderByDescending(file => file.Size)
                .ToList();

            return new Source(
                key,
                text,
                owner?.Name,
                Parts.Where(part => part.Directory is not null).Select(part => part.Directory!).ToList(),
                files,
                Bytes,
                files.Count == 0 ? DateTime.MinValue : files.Max(file => file.LastWriteUtc),
                maybeUseful);
        }
    }

    /// <summary>One folder directly under a root, or the loose files of one root.</summary>
    private sealed class Bucket(string key, string label, string? directory)
    {
        private readonly string? _folderName = directory is null ? null : label;

        public string Key { get; } = key;

        public string Label { get; } = label;

        public string? Directory { get; } = directory;

        public List<ScannedFile> Files { get; } = [];

        public long Bytes { get; private set; }

        public Owner? Owner { get; private set; }

        public void Add(ScannedFile file)
        {
            Files.Add(file);
            Bytes += file.Size;
            if (_folderName is not null)
            {
                Owner ??= Owners.Recognise(_folderName, file.Path);
            }
        }
    }
}
