using System.Text;
using CDriveCleaner.Core.Rules;

namespace CDriveCleaner.Tests;

public class RuleFileLoaderTests
{
    private const string ValidRule =
        """{"id":"r1","name":"n","group":"g","kind":"files","targets":[{"root":"%TEMP%"}]}""";

    private static RuleSet Parse(string json) => RuleFileLoader.Parse(Encoding.UTF8.GetBytes(json));

    private static string Document(string rule) => $$"""{"format":1,"rules":[{{rule}}]}""";

    [Fact]
    public void Loads_a_minimal_rule_with_defaults()
    {
        var rule = Assert.Single(Parse(Document(ValidRule)).Rules);
        Assert.Equal(RuleKind.Files, rule.Kind);
        Assert.Equal(RiskLevel.Low, rule.Risk);
        Assert.False(rule.DefaultSelected);
        Assert.Equal(0, rule.MinAgeHours);
        Assert.Empty(Assert.Single(rule.Targets).Include);
    }

    [Fact]
    public void Ships_rule_file_loads()
    {
        string path = Path.Combine(RepositoryRoot(), "cdrive-cleaner-kit", "rules", "rules.json");
        var rules = RuleFileLoader.Load(path);
        Assert.Equal(55, rules.Rules.Count);
        Assert.Equal(RuleKind.RecycleBin, rules.Find("recycle-bin")!.Kind);

        // The original 13 rules keep their ids (the translated table only added rules around them).
        string[] original =
        [
            "user-temp", "system-temp", "service-temp", "store-app-temp", "error-reports", "windows-logs", "crash-dumps",
            "inet-cache", "rdp-cache", "thumbnail-cache", "prefetch", "update-download", "recycle-bin",
        ];
        Assert.All(original, id => Assert.NotNull(rules.Find(id)));

        // Anything with a side effect is opt-in and says what the user gives up.
        Assert.All(rules.Rules.Where(rule => rule.Risk == RiskLevel.Medium), rule =>
        {
            Assert.False(rule.DefaultSelected, rule.Id);
            Assert.False(string.IsNullOrWhiteSpace(rule.Note), rule.Id);
        });
    }

    [Fact]
    public void Accepts_a_byte_order_mark_and_a_trailing_separator_on_the_root()
    {
        byte[] bytes = [.. "\uFEFF"u8, .. Encoding.UTF8.GetBytes(Document(
            """{"id":"r1","name":"n","group":"g","kind":"files","targets":[{"root":"%SystemDrive%/"}]}"""))];
        Assert.Single(RuleFileLoader.Parse(bytes).Rules);
    }

    [Fact]
    public void Accepts_format_written_as_a_float()
    {
        Assert.Single(Parse($$"""{"format":1.0,"rules":[{{ValidRule}}]}""").Rules);
    }

    [Theory]
    [InlineData("""{"id":"a\n","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x"}]}""")] // trailing newline in id
    [InlineData("""{"id":"-a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x"}]}""")]
    [InlineData("""{"id":"a","name":"","group":"g","kind":"files","targets":[{"root":"C:/x"}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","risk":"high","targets":[{"root":"C:/x"}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","default_selected":"yes","targets":[{"root":"C:/x"}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","min_age_hours":"24","targets":[{"root":"C:/x"}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"recycle_bin","min_age_hours":1}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files"}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x","exclude":["a"],"exclude":[]}]}""")] // duplicate key
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"   "}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x//y"}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x","include":["a/./b"]}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x","include":["%TEMP%"]}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x","include":"*.log"}]}""")]
    [InlineData("""{"id":"a","name":"n","group":"g","kind":"files","targets":[{"root":"C:/x","include":[""]}]}""")]
    public void Rejects_the_whole_file(string rule)
    {
        Assert.Throws<RuleFileException>(() => Parse(Document(rule)));
    }

    [Theory]
    [InlineData("""{"format":1,"rules":[],"extra":1}""")]
    [InlineData("""{"format":1,"rules":{}}""")]
    [InlineData("""{"format":true,"rules":[]}""")]
    [InlineData("""{"format":1,"rules":[], }""")]
    [InlineData("// comment\n{\"format\":1,\"rules\":[]}")]
    [InlineData("""{"$schema":5,"format":1,"rules":[]}""")]
    public void Rejects_malformed_top_levels(string json)
    {
        Assert.Throws<RuleFileException>(() => Parse(json));
    }

    [Fact]
    public void Rejects_invalid_utf8()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("""{"format":1,"rules":[{"id":"a","name":"""), 0x22, 0xC3, 0x28, 0x22, .. Encoding.UTF8.GetBytes(""","group":"g","kind":"recycle_bin"}]}""")];
        Assert.Throws<RuleFileException>(() => RuleFileLoader.Parse(bytes));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CDriveCleaner.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
