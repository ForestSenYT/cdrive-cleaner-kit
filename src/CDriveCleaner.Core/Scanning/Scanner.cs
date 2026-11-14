using CDriveCleaner.Core.Paths;
using CDriveCleaner.Core.Rules;

namespace CDriveCleaner.Core.Scanning;

/// <summary>
/// Read-only scan (SPEC sections 3 to 6). Nothing is created, changed, deleted or opened: whether a file is in use
/// is decided one file at a time during clean.
/// </summary>
/// <remarks>
/// Rules are matched in parallel, each on its own, because matching one rule never depends on another. Ownership
/// (SPEC section 5) is then settled in rule-file order on one thread, so the report is the same as a sequential scan.
/// </remarks>
public sealed class Scanner
{
    /// <summary>Directory listing is I/O-bound; a few concurrent rules keep an SSD busy without flooding a hard disk.</summary>
    private static readonly int Parallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);

    public ScanReport Scan(RuleSet rules, IProgress<ScanProgress>? progress = null, CancellationToken cancellation = default)
    {
        var protectedDirectories = ProtectedDirectories.FromEnvironment();
        DateTime now = DateTime.UtcNow;
        int count = rules.Rules.Count;
        var matches = new RuleMatches[count];
        int completed = 0;

        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellation };
        Parallel.For(0, count, options, index =>
        {
            var rule = rules.Rules[index];
            matches[index] = rule.Kind == RuleKind.RecycleBin
                ? RuleMatches.Empty
                : Collect(rule, protectedDirectories, cancellation);
            progress?.Report(new ScanProgress(Interlocked.Increment(ref completed), count, rule));
        });

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<RuleScanResult>(count);
        for (int index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var result = new RuleScanResult(rules.Rules[index]);
            if (result.Rule.Kind == RuleKind.RecycleBin)
            {
                ScanRecycleBin(result);
            }
            else
            {
                Claim(result, matches[index], claimed, now);
            }

            results.Add(result);
        }

        return new ScanReport(results);
    }

    private static void ScanRecycleBin(RuleScanResult result)
    {
        if (RecycleBin.Query() is { } answer)
        {
            result.Files = answer.Items;
            result.Bytes = answer.Bytes;
        }
        else
        {
            result.Warnings.Add(new ScanWarning(WarningCodes.RecycleBinQueryFailed, null, ""));
        }
    }

    /// <summary>Everything one rule's targets select, before ownership and age are considered.</summary>
    private static RuleMatches Collect(Rule rule, ProtectedDirectories protectedDirectories, CancellationToken cancellation)
    {
        var matched = new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<ScanWarning>();

        for (int targetIndex = 0; targetIndex < rule.Targets.Count; targetIndex++)
        {
            var target = rule.Targets[targetIndex];
            int index = targetIndex;
            void Warn(string code, string detail) => warnings.Add(new ScanWarning(code, index, detail));

            var missing = new List<string>();
            string expanded = RootExpander.ExpandVariables(target.Root, missing);
            if (missing.Count > 0)
            {
                Warn(WarningCodes.UnresolvedVariable, string.Join(",", missing));
                continue;
            }

            var include = target.Include.Select(SegmentPattern.ParsePath).ToList();
            var exclude = target.Exclude.Select(SegmentPattern.ParsePath).ToList();
            var resolver = new TargetResolver(protectedDirectories, Warn, cancellation);

            foreach (string root in RootExpander.Resolve(expanded, Warn, cancellation))
            {
                int prefixLength = root.EndsWith(PathText.Separator) ? root.Length : root.Length + 1;
                foreach (var file in resolver.Resolve(root, include))
                {
                    if (exclude.Count == 0 || !IsExcluded(file.Path[prefixLength..].Split(PathText.Separator), exclude))
                    {
                        matched.TryAdd(file.Path, new ScannedFile(file.Path, file.Size, root, file.LastWriteUtc));
                    }
                }
            }
        }

        return new RuleMatches(matched, warnings);
    }

    private static void Claim(RuleScanResult result, RuleMatches matches, HashSet<string> claimed, DateTime now)
    {
        result.Warnings.AddRange(matches.Warnings);
        double minimumAgeHours = result.Rule.MinAgeHours;
        foreach (var file in matches.Files.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            // The first rule that selects a file owns it, whatever that rule's own filters then decide (SPEC section 5).
            if (!claimed.Add(file.Path))
            {
                continue;
            }

            if (minimumAgeHours > 0 && (now - file.LastWriteUtc).TotalHours < minimumAgeHours)
            {
                result.SkippedTooNew++;
                continue;
            }

            result.Files++;
            result.Bytes += file.Size;
            result.FileList.Add(file);
        }
    }

    /// <summary>An exclude pattern of n segments excludes a file when the first n segments of its relative path match.</summary>
    private static bool IsExcluded(string[] relative, List<SegmentPattern[]> exclude)
    {
        foreach (var pattern in exclude)
        {
            if (pattern.Length > relative.Length)
            {
                continue;
            }

            bool all = true;
            for (int index = 0; index < pattern.Length && all; index++)
            {
                all = pattern[index].Matches(relative[index]);
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record RuleMatches(Dictionary<string, ScannedFile> Files, List<ScanWarning> Warnings)
    {
        public static RuleMatches Empty { get; } = new(new Dictionary<string, ScannedFile>(), []);
    }
}
