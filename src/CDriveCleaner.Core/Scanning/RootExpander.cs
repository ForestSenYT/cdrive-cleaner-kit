using System.Text.RegularExpressions;
using CDriveCleaner.Core.Paths;

namespace CDriveCleaner.Core.Scanning;

/// <summary>Turns a target's <c>root</c> into zero or more existing directories (SPEC section 3).</summary>
internal static partial class RootExpander
{
    [GeneratedRegex("%([^%]+)%", RegexOptions.CultureInvariant)]
    private static partial Regex Variable();

    /// <summary>Replaces every %NAME% (names ignore case) in one pass. Undefined names are collected in <paramref name="missing"/>.</summary>
    public static string ExpandVariables(string text, List<string> missing)
    {
        return Variable().Replace(text, match =>
        {
            string name = match.Groups[1].Value;
            string? value = Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                if (!missing.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    missing.Add(name);
                }

                return match.Value;
            }

            return value;
        });
    }

    /// <summary>
    /// Resolves an expanded root segment by segment from the drive root. Literal segments must be existing
    /// directories that are not reparse points; wildcard segments keep matching child directories, skipping
    /// reparse points silently.
    /// </summary>
    public static List<string> Resolve(string expanded, Action<string, string> warn, CancellationToken cancellation)
    {
        string[] segments = PathText.Segments(expanded);
        if (!PathText.IsDriveSegment(segments[0]) || segments.Skip(1).Any(PathText.IsBadSegment))
        {
            warn(WarningCodes.InvalidRoot, expanded);
            return [];
        }

        var current = new List<string> { char.ToUpperInvariant(segments[0][0]) + @":\" };
        foreach (string segment in segments.Skip(1))
        {
            cancellation.ThrowIfCancellationRequested();
            var following = new List<string>();
            if (segment.Contains('*'))
            {
                var pattern = SegmentPattern.Parse(segment);
                foreach (string directory in current)
                {
                    foreach (var entry in DirectoryReader.List(directory))
                    {
                        if (entry.IsDirectory && !entry.IsReparsePoint && pattern.Matches(entry.Name))
                        {
                            following.Add(PathText.Join(directory, entry.Name));
                        }
                    }
                }
            }
            else
            {
                foreach (string directory in current)
                {
                    // A missing directory simply ends this branch; that is normal and not worth a warning.
                    if (DirectoryReader.Find(directory, segment) is not { IsDirectory: true } entry)
                    {
                        continue;
                    }

                    string candidate = PathText.Join(directory, entry.Name);
                    if (entry.IsReparsePoint)
                    {
                        warn(WarningCodes.ReparsePointInRoot, candidate);
                        continue;
                    }

                    following.Add(candidate);
                }
            }

            current = following;
        }

        return current;
    }
}
