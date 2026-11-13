using System.Text.Encodings.Web;
using System.Text.Json;
using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Cli;

/// <summary>The JSON documents of SPEC section 8, written as UTF-8 to standard output.</summary>
internal static class JsonReport
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteScan(Stream output, ScanReport report, bool listPaths)
    {
        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteNumber("format", 1);
        writer.WriteStartArray("rules");
        foreach (var rule in report.Rules)
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Rule.Id);
            writer.WriteString("kind", rule.Rule.Kind.ToJsonName());
            writer.WriteNumber("files", rule.Files);
            writer.WriteNumber("bytes", rule.Bytes);
            writer.WriteStartObject("skipped");
            writer.WriteNumber("too_new", rule.SkippedTooNew);
            writer.WriteEndObject();
            writer.WriteStartArray("warnings");
            foreach (var warning in rule.Warnings)
            {
                writer.WriteStartObject();
                writer.WriteString("code", warning.Code);
                if (warning.TargetIndex is { } target)
                {
                    writer.WriteNumber("target", target);
                }
                else
                {
                    writer.WriteNull("target");
                }

                writer.WriteString("detail", warning.Detail);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (listPaths)
            {
                writer.WriteStartArray("paths");
                foreach (var file in rule.FileList)
                {
                    writer.WriteStringValue(file.Path);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("total_files", report.TotalFiles);
        writer.WriteNumber("total_bytes", report.TotalBytes);
        writer.WriteEndObject();
        writer.Flush();
        output.Write("\n"u8);
    }

    public static void WriteClean(Stream output, CleanReport report)
    {
        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteNumber("format", 1);
        writer.WriteStartArray("rules");
        foreach (var rule in report.Rules)
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Rule.Id);
            writer.WriteString("kind", rule.Rule.Kind.ToJsonName());
            writer.WriteNumber("deleted_files", rule.DeletedFiles);
            writer.WriteNumber("deleted_bytes", rule.DeletedBytes);
            writer.WriteStartArray("failed");
            foreach (var failure in rule.Failed)
            {
                writer.WriteStartObject();
                writer.WriteString("path", failure.Path);
                writer.WriteString("error", failure.Error);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("total_deleted_files", report.TotalDeletedFiles);
        writer.WriteNumber("total_deleted_bytes", report.TotalDeletedBytes);
        writer.WriteEndObject();
        writer.Flush();
        output.Write("\n"u8);
    }
}
