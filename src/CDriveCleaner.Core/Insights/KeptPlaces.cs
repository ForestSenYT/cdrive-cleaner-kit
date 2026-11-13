using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Core.Insights;

/// <summary>
/// What the user chose to keep inside one rule, recorded as places on disk: folders directly under a scanned root,
/// and roots whose loose files are kept. Because these are paths and not breakdown entries, a choice means the same
/// files after any rescan, however the breakdown then groups, folds or labels them.
/// </summary>
public sealed class KeptPlaces
{
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _looseRoots = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => _folders.Count == 0 && _looseRoots.Count == 0;

    public bool Keeps(ScannedFile file) => SourceBreakdown.FolderOf(file) is { } folder
        ? _folders.Contains(folder)
        : _looseRoots.Contains(file.Root);

    /// <summary>True when every place of the source is kept, false when none is, null when only some are.</summary>
    public bool? Covers(Source source)
    {
        int kept = source.Directories.Count(_folders.Contains) + source.LooseRoots.Count(_looseRoots.Contains);
        int places = source.Directories.Count + source.LooseRoots.Count;
        return kept == 0 ? false : kept == places ? true : null;
    }

    public void Keep(Source source)
    {
        _folders.UnionWith(source.Directories);
        _looseRoots.UnionWith(source.LooseRoots);
    }

    public void Release(Source source)
    {
        _folders.ExceptWith(source.Directories);
        _looseRoots.ExceptWith(source.LooseRoots);
    }
}
