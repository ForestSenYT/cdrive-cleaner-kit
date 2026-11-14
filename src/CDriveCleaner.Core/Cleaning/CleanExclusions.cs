using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Core.Cleaning;

/// <summary>
/// Files the user decided to keep although their rule is selected, for example everything one program left in the
/// temp folder. Built once when the user confirms, then only read, so a running clean never sees it change.
/// </summary>
public sealed class CleanExclusions
{
    private readonly Dictionary<string, HashSet<string>> _byRule;

    private CleanExclusions(Dictionary<string, HashSet<string>> byRule)
    {
        _byRule = byRule;
    }

    public static CleanExclusions None { get; } = new(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));

    /// <param name="kept">Rule id and the files of that rule's scan list to leave alone.</param>
    public static CleanExclusions From(IEnumerable<(string RuleId, IEnumerable<ScannedFile> Files)> kept)
    {
        var byRule = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (ruleId, files) in kept)
        {
            if (!byRule.TryGetValue(ruleId, out var paths))
            {
                paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byRule.Add(ruleId, paths);
            }

            foreach (var file in files)
            {
                paths.Add(file.Path);
            }
        }

        return new CleanExclusions(byRule);
    }

    public bool IsEmpty => _byRule.Values.All(paths => paths.Count == 0);

    public bool Keeps(string ruleId, string path) => _byRule.TryGetValue(ruleId, out var paths) && paths.Contains(path);
}
