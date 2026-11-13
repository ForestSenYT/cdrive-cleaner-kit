using System.Text.Json;
using System.Text.Unicode;
using CDriveCleaner.Core.Paths;

namespace CDriveCleaner.Core.Rules;

/// <summary>
/// Loads and validates a rule file (SPEC section 2, rules.schema.json). Any violation rejects the whole file:
/// a misspelt <c>exclude</c> silently ignored would widen what gets deleted, so nothing unknown is tolerated.
/// </summary>
public static class RuleFileLoader
{
    private static readonly string[] TopKeys = ["$schema", "format", "rules"];
    private static readonly string[] RuleKeys =
        ["id", "name", "group", "description", "note", "risk", "default_selected", "kind", "min_age_hours", "targets"];
    private static readonly string[] TargetKeys = ["root", "include", "exclude"];

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    public static RuleSet Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new RuleFileException($"无法读取规则文件：{error.Message}", error);
        }

        return Parse(bytes);
    }

    public static RuleSet Parse(byte[] utf8)
    {
        ReadOnlyMemory<byte> data = utf8;
        if (data.Span.StartsWith("﻿"u8))
        {
            data = data[3..];
        }

        if (!Utf8.IsValid(data.Span))
        {
            throw Reject("规则文件不是合法的 UTF-8 文本");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data, ParseOptions);
        }
        catch (JsonException error)
        {
            throw new RuleFileException($"规则文件不是合法 JSON：{error.Message}", error);
        }

        using (document)
        {
            return ParseDocument(document.RootElement);
        }
    }

    private static RuleSet ParseDocument(JsonElement root)
    {
        var fields = Fields(root, TopKeys, "顶层");
        if (fields.TryGetValue("$schema", out var schema) && schema.ValueKind != JsonValueKind.String)
        {
            throw Reject("顶层：$schema 必须是字符串");
        }

        if (!fields.TryGetValue("format", out var format)
            || format.ValueKind != JsonValueKind.Number
            || !format.TryGetDouble(out double formatValue)
            || formatValue != 1)
        {
            throw Reject("顶层：format 必须为 1");
        }

        if (!fields.TryGetValue("rules", out var rulesElement) || rulesElement.ValueKind != JsonValueKind.Array)
        {
            throw Reject("顶层：rules 必须是数组");
        }

        var rules = new List<Rule>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var element in rulesElement.EnumerateArray())
        {
            var rule = ParseRule(element, $"rules[{index}]");
            if (!seenIds.Add(rule.Id))
            {
                throw Reject($"rules[{index}]：id \"{rule.Id}\" 与前面的规则重复");
            }

            rules.Add(rule);
            index++;
        }

        return new RuleSet(rules);
    }

    private static Rule ParseRule(JsonElement element, string where)
    {
        var fields = Fields(element, RuleKeys, where);
        string id = RequiredText(fields, "id", where);
        string name = RequiredText(fields, "name", where);
        string group = RequiredText(fields, "group", where);
        string kindText = RequiredText(fields, "kind", where);
        if (!IsValidId(id))
        {
            throw Reject($"{where}：id \"{id}\" 只能由小写字母、数字和连字符组成，并以字母或数字开头");
        }

        string? description = OptionalText(fields, "description", where);
        string? note = OptionalText(fields, "note", where);
        var risk = OptionalText(fields, "risk", where) switch
        {
            null or "low" => RiskLevel.Low,
            "medium" => RiskLevel.Medium,
            _ => throw Reject($"{where}：risk 只能是 low 或 medium"),
        };

        bool defaultSelected = false;
        if (fields.TryGetValue("default_selected", out var selected))
        {
            defaultSelected = selected.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Reject($"{where}：default_selected 必须是布尔值"),
            };
        }

        RuleKind kind = RuleKindNames.FromJsonName(kindText)
            ?? throw Reject($"{where}：未知的 kind \"{kindText}\"（只能是 files 或 recycle_bin）");
        if (kind == RuleKind.RecycleBin)
        {
            if (fields.ContainsKey("targets") || fields.ContainsKey("min_age_hours"))
            {
                throw Reject($"{where}：recycle_bin 规则不能带 targets 或 min_age_hours");
            }

            return new Rule(id, name, group, RuleKind.RecycleBin, description, note, risk, defaultSelected, 0, []);
        }

        double minAgeHours = 0;
        if (fields.TryGetValue("min_age_hours", out var age))
        {
            if (age.ValueKind != JsonValueKind.Number || !age.TryGetDouble(out minAgeHours)
                || !double.IsFinite(minAgeHours) || minAgeHours < 0)
            {
                throw Reject($"{where}：min_age_hours 必须是不小于 0 的数值");
            }
        }

        if (!fields.TryGetValue("targets", out var targetsElement)
            || targetsElement.ValueKind != JsonValueKind.Array
            || targetsElement.GetArrayLength() == 0)
        {
            throw Reject($"{where}：files 规则至少要有一个目标（targets）");
        }

        var targets = new List<Target>();
        int number = 0;
        foreach (var targetElement in targetsElement.EnumerateArray())
        {
            targets.Add(ParseTarget(targetElement, $"{where}.targets[{number}]"));
            number++;
        }

        return new Rule(id, name, group, RuleKind.Files, description, note, risk, defaultSelected, minAgeHours, targets);
    }

    private static Target ParseTarget(JsonElement element, string where)
    {
        var fields = Fields(element, TargetKeys, where);
        if (!fields.TryGetValue("root", out var rootElement)
            || rootElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(rootElement.GetString()))
        {
            throw Reject($"{where}：root 必须是非空字符串");
        }

        string root = rootElement.GetString()!;
        CheckRoot(root, where);
        return new Target(root, Patterns(fields, "include", where), Patterns(fields, "exclude", where));
    }

    private static List<string> Patterns(Dictionary<string, JsonElement> fields, string key, string where)
    {
        var result = new List<string>();
        if (!fields.TryGetValue(key, out var array))
        {
            return result;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw Reject($"{where}：{key} 必须是数组");
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw Reject($"{where}.{key}：模式必须是非空字符串");
            }

            string pattern = item.GetString()!;
            CheckPattern(pattern, $"{where}.{key}");
            result.Add(pattern);
        }

        return result;
    }

    /// <summary>
    /// No segment may be empty, "." or ".."; the first segment is a drive (<c>C:</c>) or starts with '%',
    /// and never contains '*'. A trailing separator is tolerated (<c>%SystemDrive%/</c>).
    /// </summary>
    private static void CheckRoot(string root, string where)
    {
        string[] segments = PathText.Segments(root);
        if (segments.Any(PathText.IsBadSegment))
        {
            throw Reject($"{where}：root \"{root}\" 不能含空段、\".\" 或 \"..\"");
        }

        string first = segments[0];
        if (first.Contains('*') || !(first.StartsWith('%') || PathText.IsDriveSegment(first)))
        {
            throw Reject($"{where}：root \"{root}\" 必须以盘符（如 C:）或 %变量% 开头，且第一段不能含 *");
        }
    }

    /// <summary>Patterns are relative: no empty, "." or ".." segment (so no leading separator), no ':' and no '%'.</summary>
    private static void CheckPattern(string pattern, string where)
    {
        foreach (string segment in PathText.Segments(pattern))
        {
            if (PathText.IsBadSegment(segment))
            {
                throw Reject($"{where}：模式 \"{pattern}\" 必须是相对路径，不能含空段、\".\" 或 \"..\"");
            }

            if (segment.Contains(':') || segment.Contains('%'))
            {
                throw Reject($"{where}：模式 \"{pattern}\" 不能含 ':' 或 '%'");
            }
        }
    }

    private static bool IsValidId(string id) =>
        id.Length > 0 && IsLowerAlphanumeric(id[0]) && id.All(c => IsLowerAlphanumeric(c) || c == '-');

    private static bool IsLowerAlphanumeric(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';

    /// <summary>The object's members, rejecting unknown and duplicated names (a duplicate could silently replace a list).</summary>
    private static Dictionary<string, JsonElement> Fields(JsonElement element, string[] allowed, string where)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Reject($"{where}：必须是 JSON 对象");
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                throw Reject($"{where}：未知字段 \"{property.Name}\"");
            }

            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Reject($"{where}：字段 \"{property.Name}\" 重复出现");
            }
        }

        return result;
    }

    private static string RequiredText(Dictionary<string, JsonElement> fields, string key, string where)
    {
        if (!fields.TryGetValue(key, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } text)
        {
            throw Reject($"{where}：必填字段 {key} 必须是非空字符串");
        }

        return text;
    }

    private static string? OptionalText(Dictionary<string, JsonElement> fields, string key, string where)
    {
        if (!fields.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Reject($"{where}：{key} 必须是字符串");
        }

        return value.GetString();
    }

    private static RuleFileException Reject(string message) => new(message);
}
