using CDriveCleaner.Core.Paths;
using CDriveCleaner.Core.Rules;

namespace CDriveCleaner.Core.Scanning;

/// <summary>
/// Read-only scan (SPEC sections 3 to 6). Nothing is created, changed, deleted or opened: whether a file is in use
/// is decided one file at a time during clean.
/// </summary>
public sealed class Scanner
{
    public ScanReport Scan(RuleSet rules, IProgress<ScanProgress>? progress = null, CancellationToken cancellation = default)
    {
        var protectedDirectories = ProtectedDirectories.FromEnvironment();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime now = DateTime.UtcNow;
        var results = new List<RuleScanResult>(rules.Rules.Count);

        for (int index = 0; index < rules.Rules.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var rule = rules.Rules[index];
            progress?.Report(new ScanProgress(index, rules.Rules.Count, rule));
            var result = new RuleScanResult(rule);
            if (rule.Kind == RuleKind.RecycleBin)
            {
                ScanRecycleBin(result);
            }
            else
            {
                ScanFiles(result, protectedDirectories, claimed, now, cancellation);
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

    private static void ScanFiles(
        RuleScanResult result, ProtectedDirectories protectedDirectories, HashSet<string> claimed, DateTime now,
        CancellationToken cancellation)
    {
        var rule = result.Rule;
        var matched = new Dictionary<string, FoundFile>(StringComparer.OrdinalIgnoreCase);

        for (int targetIndex = 0; targetIndex < rule.Targets.Count; targetIndex++)
        {
            var target = rule.Targets[targetIndex];
            int index = targetIndex;
            void Warn(string code, string detail) => result.Warnings.Add(new ScanWarning(code, index, detail));

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
                string prefix = root.EndsWith(PathText.Separator) ? root : root + PathText.Separator;
                foreach (var file in resolver.Resolve(root, include))
                {
                    string[] relative = file.Path[prefix.Length..].Split(PathText.Separator);
                    if (!IsExcluded(relative, exclude))
                    {
                        matched.TryAdd(file.Path, file);
                    }
                }
            }
        }

        double minimumAgeHours = rule.MinAgeHours;
        foreach (var file in matched.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
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
            result.FileList.Add(new ScannedFile(file.Path, file.Size));
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
}
