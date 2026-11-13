using CDriveCleaner.Core.Paths;

namespace CDriveCleaner.Core.Scanning;

internal readonly record struct FoundFile(string Path, long Size, DateTime LastWriteUtc);

/// <summary>The files one concrete root selects (SPEC section 4).</summary>
internal sealed class TargetResolver
{
    /// <summary>Directories of one tree level are listed concurrently; listing is I/O-bound.</summary>
    private static readonly int WalkParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);

    private readonly ProtectedDirectories _protected;
    private readonly Action<string, string> _warn;
    private readonly CancellationToken _cancellation;

    public TargetResolver(ProtectedDirectories protectedDirectories, Action<string, string> warn, CancellationToken cancellation)
    {
        _protected = protectedDirectories;
        _warn = warn;
        _cancellation = cancellation;
    }

    /// <summary>Everything below <paramref name="root"/> when there is no include pattern, otherwise what each pattern selects.</summary>
    public List<FoundFile> Resolve(string root, IReadOnlyList<SegmentPattern[]> include)
    {
        var found = new List<FoundFile>();
        if (include.Count == 0)
        {
            WalkGuarded(root, found);
            return found;
        }

        foreach (var pattern in include)
        {
            if (_protected.Contains(root) && pattern.All(segment => segment.IsAllStars))
            {
                _warn(WarningCodes.ProtectedRoot, root);
                continue;
            }

            var parents = new List<string> { root };
            foreach (var segment in pattern[..^1])
            {
                var following = new List<string>();
                foreach (string directory in parents)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    following.AddRange(DirectoryReader.MatchingSubdirectories(directory, segment));
                }

                parents = following;
            }

            var last = pattern[^1];
            foreach (string directory in parents)
            {
                _cancellation.ThrowIfCancellationRequested();
                foreach (var entry in DirectoryReader.List(directory))
                {
                    if (entry.IsReparsePoint || !last.Matches(entry.Name))
                    {
                        continue;
                    }

                    string child = PathText.Join(directory, entry.Name);
                    if (entry.IsDirectory)
                    {
                        WalkGuarded(child, found);
                    }
                    else
                    {
                        found.Add(new FoundFile(child, entry.Length, entry.LastWriteUtc));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Every regular file below <paramref name="top"/>. Reparse points are neither entered nor reported, and a
    /// protected directory is never walked, whether it is the starting point or met on the way down. The tree is
    /// walked level by level with the directories of a level listed concurrently; the protected directories met on
    /// the way are reported in path order, so the result does not depend on timing.
    /// </summary>
    private void WalkGuarded(string top, List<FoundFile> found)
    {
        if (_protected.Contains(top))
        {
            _warn(WarningCodes.ProtectedRoot, top);
            return;
        }

        var refused = new List<string>();
        var gate = new object();
        var options = new ParallelOptions { MaxDegreeOfParallelism = WalkParallelism, CancellationToken = _cancellation };
        var level = new List<string> { top };
        while (level.Count > 0)
        {
            var next = new List<string>();
            Parallel.ForEach(
                level,
                options,
                () => new Listing(),
                (directory, _, listing) =>
                {
                    foreach (var entry in DirectoryReader.List(directory))
                    {
                        if (entry.IsReparsePoint)
                        {
                            continue;
                        }

                        string child = PathText.Join(directory, entry.Name);
                        if (!entry.IsDirectory)
                        {
                            listing.Files.Add(new FoundFile(child, entry.Length, entry.LastWriteUtc));
                        }
                        else if (_protected.Contains(child))
                        {
                            listing.Refused.Add(child);
                        }
                        else
                        {
                            listing.Directories.Add(child);
                        }
                    }

                    return listing;
                },
                listing =>
                {
                    lock (gate)
                    {
                        found.AddRange(listing.Files);
                        next.AddRange(listing.Directories);
                        refused.AddRange(listing.Refused);
                    }
                });
            level = next;
        }

        refused.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in refused)
        {
            _warn(WarningCodes.ProtectedRoot, directory);
        }
    }

    private sealed class Listing
    {
        public List<FoundFile> Files { get; } = [];

        public List<string> Directories { get; } = [];

        public List<string> Refused { get; } = [];
    }
}
