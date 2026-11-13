using CDriveCleaner.Core.Paths;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Tests;

/// <summary>Small pieces of text both front ends rely on, so the CLI and the window can never drift apart.</summary>
public class SharedTextTests
{
    [Theory]
    [InlineData(0, "目标 1")]
    [InlineData(2, "目标 3")]
    [InlineData(null, "回收站")]
    public void Warning_location_numbers_targets_from_one(int? targetIndex, string expected)
    {
        Assert.Equal(expected, new ScanWarning(WarningCodes.InvalidRoot, targetIndex, "").Location);
    }

    [Theory]
    [InlineData(RuleKind.Files, "files")]
    [InlineData(RuleKind.RecycleBin, "recycle_bin")]
    public void Rule_kind_names_round_trip(RuleKind kind, string name)
    {
        Assert.Equal(name, kind.ToJsonName());
        Assert.Equal(kind, RuleKindNames.FromJsonName(name));
    }

    [Theory]
    [InlineData("Files")]
    [InlineData("recycle-bin")]
    [InlineData("")]
    public void Unknown_rule_kind_names_are_not_guessed(string name)
    {
        Assert.Null(RuleKindNames.FromJsonName(name));
    }

    [Theory]
    [InlineData("C:/Windows/", @"C:\Windows")]
    [InlineData(@"C:\Windows\\", @"C:\Windows")]
    [InlineData("C:/", "C:")]
    public void Normalize_unifies_separators_and_drops_trailing_ones(string path, string expected)
    {
        Assert.Equal(expected, PathText.Normalize(path));
    }

    [Theory]
    [InlineData(@"\\?\C:\Windows", @"C:\Windows")]
    [InlineData(@"C:\Windows", @"C:\Windows")]
    public void Extended_prefix_round_trips(string path, string plain)
    {
        Assert.Equal(plain, PathText.WithoutExtendedPrefix(path));
        Assert.Equal(plain, PathText.WithoutExtendedPrefix(PathText.Extended(plain)));
    }
}
