using CDriveCleaner.Core.Paths;

namespace CDriveCleaner.Tests;

public class SegmentPatternTests
{
    [Theory]
    [InlineData("*.log", "x.log", true)]
    [InlineData("*.log", "X.LOG", true)]
    [InlineData("*.log", ".log", true)]
    [InlineData("*.log", "x.log.txt", false)]
    [InlineData("app_*", "app_", true)]
    [InlineData("app_*", "apple", false)]
    [InlineData("thumbcache_*.db", "thumbcache_1024.db", true)]
    [InlineData("a*a", "a", false)]
    [InlineData("a*a", "aa", true)]
    [InlineData("a*b*c", "abc", true)]
    [InlineData("a*b*c", "acb", false)]
    [InlineData("a*b*c", "aXbYbZc", true)]
    [InlineData("*", "", true)]
    [InlineData("**", "anything", true)]
    [InlineData("MEMORY.DMP", "memory.dmp", true)]
    [InlineData("a?c", "abc", false)] // '?' is literal (SPEC section 3)
    [InlineData("[ab]", "a", false)] // so are brackets
    [InlineData("[ab]", "[AB]", true)]
    public void Matches_follows_the_single_wildcard_rules(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, SegmentPattern.Parse(pattern).Matches(name));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("***", true)]
    [InlineData("*.log", false)]
    [InlineData("Minidump", false)]
    public void IsAllStars_recognises_star_only_segments(string pattern, bool expected)
    {
        Assert.Equal(expected, SegmentPattern.Parse(pattern).IsAllStars);
    }

    [Fact]
    public void ParsePath_treats_both_separators_alike_and_ignores_a_trailing_one()
    {
        var segments = SegmentPattern.ParsePath(@"Panther/sub\*.log/");
        Assert.Equal(["Panther", "sub", "*.log"], segments.Select(segment => segment.Text));
    }
}
