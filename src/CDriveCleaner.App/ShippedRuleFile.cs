using System.IO;

namespace CDriveCleaner.App;

/// <summary>
/// The shipped rule file is also compiled into the program. The installed program reads <c>rules\rules.json</c> next
/// to the executable, where anyone can read and audit it; the portable build is a single file with no such folder, so
/// it uses the compiled-in copy, which is byte for byte the same file.
/// </summary>
internal static class ShippedRuleFile
{
    private const string ResourceName = "rules.json";

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "rules", "rules.json");

    public static byte[] Embedded()
    {
        using var stream = typeof(ShippedRuleFile).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("程序里缺少内置的规则文件。");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
