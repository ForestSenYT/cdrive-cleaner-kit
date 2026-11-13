namespace CDriveCleaner.Core.Rules;

public enum RuleKind
{
    Files,
    RecycleBin,
}

public enum RiskLevel
{
    Low,
    Medium,
}

/// <summary>One <c>{root, include, exclude}</c> entry of a rule (SPEC section 1, "目标").</summary>
public sealed record Target(string Root, IReadOnlyList<string> Include, IReadOnlyList<string> Exclude);

/// <summary>A rule as loaded from the rule file; the unit the user selects.</summary>
public sealed record Rule(
    string Id,
    string Name,
    string Group,
    RuleKind Kind,
    string? Description,
    string? Note,
    RiskLevel Risk,
    bool DefaultSelected,
    double MinAgeHours,
    IReadOnlyList<Target> Targets);

/// <summary>A validated rule file. Rule order is meaningful (SPEC section 5: first matching rule owns a file).</summary>
public sealed record RuleSet(IReadOnlyList<Rule> Rules)
{
    public Rule? Find(string id) => Rules.FirstOrDefault(rule => rule.Id == id);
}

/// <summary>The <c>kind</c> spellings of the rule file and the JSON report; the one place they are written down.</summary>
public static class RuleKindNames
{
    private const string Files = "files";
    private const string RecycleBin = "recycle_bin";

    public static string ToJsonName(this RuleKind kind) => kind switch
    {
        RuleKind.Files => Files,
        RuleKind.RecycleBin => RecycleBin,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The kind a rule file names, or null for an unknown spelling (matching is exact).</summary>
    public static RuleKind? FromJsonName(string name) => name switch
    {
        Files => RuleKind.Files,
        RecycleBin => RuleKind.RecycleBin,
        _ => null,
    };
}
