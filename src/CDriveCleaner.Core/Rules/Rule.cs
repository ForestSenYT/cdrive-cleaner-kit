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

public static class RuleKindNames
{
    public static string ToJsonName(this RuleKind kind) => kind switch
    {
        RuleKind.Files => "files",
        RuleKind.RecycleBin => "recycle_bin",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
