using System.Text;
using CDriveCleaner.Cli;
using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

// Command-line contract of SPEC section 8. Exit code 0: the command ran (warnings and failed deletions included).
// Exit code 2: rejected rule file, bad arguments, unknown --select id, or clean without --yes; nothing is scanned or deleted.
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
TextWriter error = Console.IsErrorRedirected
    ? new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true }
    : Console.Error;

try
{
    if (!Environment.Is64BitProcess)
    {
        error.WriteLine("错误：必须以 64 位进程运行。");
        return 2;
    }

    var options = CommandLine.Parse(args);
    if (options.Command == Command.Help)
    {
        Console.Out.WriteLine(CommandLine.Usage);
        return 0;
    }

    var rules = RuleFileLoader.Load(options.RulesPath);

    if (options.Command == Command.Clean)
    {
        if (options.Select.Count == 0)
        {
            throw new UsageException("--select 至少要指定一个规则 id。");
        }

        var unknown = options.Select.Where(id => rules.Find(id) is null).ToList();
        if (unknown.Count > 0)
        {
            throw new UsageException($"--select 含有规则文件里没有的 id：{string.Join(", ", unknown)}");
        }

        if (!options.Yes)
        {
            throw new UsageException("clean 会永久删除文件，请加 --yes 确认。");
        }
    }

    // clean scans first, then deletes the selected rules' lists, in the same process.
    var scan = new Scanner().Scan(rules);
    using var stdout = Console.OpenStandardOutput();
    if (options.Command == Command.Scan)
    {
        if (options.Json)
        {
            JsonReport.WriteScan(stdout, scan, options.List);
        }
        else
        {
            using var text = TextOutput(stdout, utf8);
            TextReport.WriteScan(text, scan, options.List);
        }

        return 0;
    }

    var report = new Cleaner().Clean(scan, options.Select.ToHashSet(StringComparer.Ordinal));
    if (options.Json)
    {
        JsonReport.WriteClean(stdout, report);
    }
    else
    {
        using var text = TextOutput(stdout, utf8);
        TextReport.WriteClean(text, report);
    }

    return 0;
}
catch (UsageException failure)
{
    error.WriteLine($"错误：{failure.Message}");
    error.WriteLine();
    error.WriteLine(CommandLine.Usage);
    return 2;
}
catch (RuleFileException failure)
{
    error.WriteLine($"规则文件被拒绝：{failure.Message}");
    return 2;
}

// A console gets Unicode through Console.Out; a pipe or file gets UTF-8 like the JSON output.
static TextWriter TextOutput(Stream stdout, Encoding utf8) =>
    Console.IsOutputRedirected ? new StreamWriter(stdout, utf8, leaveOpen: true) : new NonClosingWriter(Console.Out);

internal sealed class NonClosingWriter(TextWriter inner) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(value);

    public override void Write(string? value) => inner.Write(value);

    public override void WriteLine(string? value) => inner.WriteLine(value);

    protected override void Dispose(bool disposing) => inner.Flush();
}
