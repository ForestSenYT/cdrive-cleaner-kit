using CDriveCleaner.Core.Paths;

namespace CDriveCleaner.Core.Scanning;

internal readonly record struct FoundFile(string Path, long Size, DateTime LastWriteUtc);

/// <summary>The files one concrete root selects (SPEC section 4).</summary>
internal sealed class TargetResolver
{
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
    public IEnumerable<FoundFile> Resolve(string root, IReadOnlyList<SegmentPattern[]> include)
    {
        if (include.Count == 0)
        {
            foreach (var file in WalkGuarded(root))
            {
                yield return file;
            }

            yield break;
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
                    if (!entry.IsDirectory)
                    {
                        yield return new FoundFile(child, entry.Length, entry.LastWriteUtc);
                        continue;
                    }

                    foreach (var file in WalkGuarded(child))
                    {
                        yield return file;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every regular file below <paramref name="top"/>. Reparse points are neither entered nor reported, and a
    /// protected directory is never walked, whether it is the starting point or met on the way down.
    /// </summary>
    private IEnumerable<FoundFile> WalkGuarded(string top)
    {
        if (_protected.Contains(top))
        {
            _warn(WarningCodes.ProtectedRoot, top);
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(top);
        while (pending.Count > 0)
        {
            _cancellation.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (var entry in DirectoryReader.List(directory))
            {
                if (entry.IsReparsePoint)
                {
                    continue;
                }

                string child = PathText.Join(directory, entry.Name);
                if (!entry.IsDirectory)
                {
                    yield return new FoundFile(child, entry.Length, entry.LastWriteUtc);
                }
                else if (_protected.Contains(child))
                {
                    _warn(WarningCodes.ProtectedRoot, child);
                }
                else
                {
                    pending.Push(child);
                }
            }
        }
    }
}
