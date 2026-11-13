using CDriveCleaner.Core.Rules;

namespace CDriveCleaner.Core.Cleaning;

/// <summary>Per-file error codes of SPEC section 7, in the order the checks run.</summary>
public static class CleanErrorCodes
{
    public const string Gone = "gone";
    public const string NotRegularFile = "not_regular_file";
    public const string InUseOrDenied = "in_use_or_denied";
    public const string DeleteFailed = "delete_failed";
}

public sealed record CleanFailure(string Path, string Error);

public sealed class RuleCleanResult
{
    internal RuleCleanResult(Rule rule)
    {
        Rule = rule;
    }

    public Rule Rule { get; }

    public long DeletedFiles { get; internal set; }

    public long DeletedBytes { get; internal set; }

    /// <summary>Files the user chose to keep (see <see cref="CleanExclusions"/>); they were not touched.</summary>
    public long KeptFiles { get; internal set; }

    public long KeptBytes { get; internal set; }

    public List<CleanFailure> Failed { get; } = [];
}

public sealed class CleanReport
{
    internal CleanReport(IReadOnlyList<RuleCleanResult> rules, bool cancelled)
    {
        Rules = rules;
        Cancelled = cancelled;
    }

    /// <summary>Only the selected rules, in rule-file order.</summary>
    public IReadOnlyList<RuleCleanResult> Rules { get; }

    /// <summary>The user stopped the clean part-way. Files already deleted stay deleted.</summary>
    public bool Cancelled { get; }

    public long TotalDeletedFiles => Rules.Sum(rule => rule.DeletedFiles);

    public long TotalDeletedBytes => Rules.Sum(rule => rule.DeletedBytes);

    /// <summary>The result for one rule, or null when that rule was not selected.</summary>
    public RuleCleanResult? Find(string id) => Rules.FirstOrDefault(result => result.Rule.Id == id);
}

/// <param name="Processed">Files handled so far across all selected rules.</param>
/// <param name="Total">Files in the selected rules' lists.</param>
public sealed record CleanProgress(long Processed, long Total, Rule Rule);
