using System.Text.Json;
using System.Text.Json.Serialization;
using CDriveCleaner.Core.Cleaning;

namespace CDriveCleaner.Core.History;

/// <param name="Id">Rule id.</param>
/// <param name="Name">Rule name at the time of the clean.</param>
public sealed record HistoryRule(string Id, string Name, long DeletedFiles, long DeletedBytes, long FailedFiles);

public sealed record HistoryEntry(DateTimeOffset Time, bool Cancelled, IReadOnlyList<HistoryRule> Rules)
{
    [JsonIgnore]
    public long DeletedFiles => Rules.Sum(rule => rule.DeletedFiles);

    [JsonIgnore]
    public long DeletedBytes => Rules.Sum(rule => rule.DeletedBytes);

    [JsonIgnore]
    public long FailedFiles => Rules.Sum(rule => rule.FailedFiles);

    public static HistoryEntry From(CleanReport report, DateTimeOffset time) => new(
        time,
        report.Cancelled,
        report.Rules.Select(rule => new HistoryRule(
            rule.Rule.Id, rule.Rule.Name, rule.DeletedFiles, rule.DeletedBytes, rule.Failed.Count)).ToList());
}

/// <summary>
/// Past cleans, newest first, kept in one small JSON file per user. History is a convenience: a missing or damaged
/// file reads as empty and a failed write is ignored, so it can never stand in the way of cleaning.
/// </summary>
public sealed class CleanHistory
{
    public const int MaxEntries = 200;

    private readonly string _path;

    public CleanHistory(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CDriveCleaner", "history.json");

    public IReadOnlyList<HistoryEntry> Load() => Read().Entries;

    /// <returns>The history including <paramref name="entry"/>, newest first.</returns>
    public IReadOnlyList<HistoryEntry> Add(HistoryEntry entry)
    {
        var (past, writable) = Read();
        var entries = new List<HistoryEntry> { entry };
        entries.AddRange(past.Take(MaxEntries - 1));
        if (!writable)
        {
            // The file exists but could not be read just now; writing would replace everything in it.
            return entries;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, entries, HistoryJson.Default.ListHistoryEntry);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Keep cleaning usable on a read-only profile; the entry is still shown for this session.
        }

        return entries;
    }

    /// <summary>The valid entries, and whether writing the file back would lose nothing that could have been read.</summary>
    private (List<HistoryEntry> Entries, bool Writable) Read()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            var entries = JsonSerializer.Deserialize(stream, HistoryJson.Default.ListHistoryEntry) ?? [];
            return (entries.Where(IsValid).ToList(), true);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return ([], true);
        }
        catch (JsonException)
        {
            // Unreadable for good: keep a copy for whoever wants to look, then start afresh.
            try
            {
                File.Copy(_path, _path + ".damaged", overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return ([], false);
            }

            return ([], true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ([], false);
        }
    }

    /// <summary>Valid JSON can still carry nulls where an entry or a rule should be; such entries are skipped.</summary>
    private static bool IsValid(HistoryEntry? entry) =>
        entry?.Rules is not null && entry.Rules.All(rule => rule is { Id: not null, Name: not null });
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(List<HistoryEntry>))]
internal sealed partial class HistoryJson : JsonSerializerContext;
