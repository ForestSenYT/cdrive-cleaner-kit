using CDriveCleaner.Core.Rules;

namespace CDriveCleaner.Core.Scanning;

/// <summary>Warning codes of SPEC section 8.</summary>
public static class WarningCodes
{
    public const string UnresolvedVariable = "unresolved_variable";
    public const string InvalidRoot = "invalid_root";
    public const string ReparsePointInRoot = "reparse_point_in_root";
    public const string ProtectedRoot = "protected_root";
    public const string RecycleBinQueryFailed = "recycle_bin_query_failed";
}

/// <param name="Target">Index of the target inside the rule; null for the recycle bin rule.</param>
public sealed record ScanWarning(string Code, int? Target, string Detail);

public sealed record ScannedFile(string Path, long Size);

/// <summary>What one rule found. <see cref="FileList"/> is the deletion list a later clean works from (SPEC section 7).</summary>
public sealed class RuleScanResult
{
    internal RuleScanResult(Rule rule)
    {
        Rule = rule;
    }

    public Rule Rule { get; }

    public long Files { get; internal set; }

    public long Bytes { get; internal set; }

    public long SkippedTooNew { get; internal set; }

    public List<ScanWarning> Warnings { get; } = [];

    public List<ScannedFile> FileList { get; } = [];
}

public sealed class ScanReport
{
    internal ScanReport(IReadOnlyList<RuleScanResult> rules)
    {
        Rules = rules;
    }

    /// <summary>One entry per rule, in rule-file order.</summary>
    public IReadOnlyList<RuleScanResult> Rules { get; }

    public long TotalFiles => Rules.Sum(rule => rule.Files);

    public long TotalBytes => Rules.Sum(rule => rule.Bytes);

    public RuleScanResult? Find(string id) => Rules.FirstOrDefault(result => result.Rule.Id == id);
}

/// <param name="RuleIndex">Zero-based index of the rule being scanned.</param>
public sealed record ScanProgress(int RuleIndex, int RuleCount, Rule Rule);
