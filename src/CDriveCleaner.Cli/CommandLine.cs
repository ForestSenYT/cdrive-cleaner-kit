namespace CDriveCleaner.Cli;

internal sealed class UsageException(string message) : Exception(message);

internal enum Command
{
    Scan,
    Clean,
    Help,
}

internal sealed record CommandLine(Command Command, string RulesPath, bool Json, bool List, IReadOnlyList<string> Select, bool Yes)
{
    public const string Usage =
        """
        用法：
          cdclean scan  --rules <文件> [--json] [--list]
          cdclean clean --rules <文件> --select <id>[,<id>...] --yes [--json]

        scan   只读扫描，报告每条规则可清理的文件数量和大小；--list 同时列出文件路径。
        clean  先扫描，再永久删除所选规则的文件；必须加 --yes 确认。
        """;

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new UsageException("缺少命令（scan 或 clean）。");
        }

        if (args.Any(arg => arg is "-h" or "--help" or "/?"))
        {
            return new CommandLine(Command.Help, "", false, false, [], false);
        }

        var command = args[0] switch
        {
            "scan" => Command.Scan,
            "clean" => Command.Clean,
            _ => throw new UsageException($"未知命令：{args[0]}"),
        };

        string? rules = null;
        string? select = null;
        bool json = false, list = false, yes = false;
        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            switch (option)
            {
                case "--rules":
                    rules = Value(args, ref index, rules);
                    break;
                case "--json":
                    json = Flag(json, option);
                    break;
                case "--list" when command == Command.Scan:
                    list = Flag(list, option);
                    break;
                case "--select" when command == Command.Clean:
                    select = Value(args, ref index, select);
                    break;
                case "--yes" when command == Command.Clean:
                    yes = Flag(yes, option);
                    break;
                default:
                    throw new UsageException($"不认识的参数：{option}");
            }
        }

        if (rules is null)
        {
            throw new UsageException("缺少 --rules <文件>。");
        }

        if (command == Command.Clean && select is null)
        {
            throw new UsageException("clean 需要 --select <id>[,<id>...]。");
        }

        var ids = (select ?? "").Split(',').Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return new CommandLine(command, rules, json, list, ids, yes);
    }

    private static string Value(string[] args, ref int index, string? previous)
    {
        string option = args[index];
        if (previous is not null)
        {
            throw new UsageException($"{option} 只能出现一次。");
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new UsageException($"{option} 后面缺少取值。");
        }

        index++;
        return args[index];
    }

    private static bool Flag(bool previous, string option)
    {
        if (previous)
        {
            throw new UsageException($"{option} 只能出现一次。");
        }

        return true;
    }
}
