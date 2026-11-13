using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;
using CDriveCleaner.Core.Text;

namespace CDriveCleaner.Cli;

/// <summary>Human-readable output: rules grouped by <c>group</c>, with risk, default selection and note.</summary>
internal static class TextReport
{
    public static void WriteScan(TextWriter output, ScanReport report, bool listPaths)
    {
        foreach (var group in report.Rules.GroupBy(result => result.Rule.Group))
        {
            output.WriteLine($"[{group.Key}]");
            foreach (var result in group)
            {
                var rule = result.Rule;
                string flags = (rule.DefaultSelected ? "  默认勾选" : "") + (rule.Risk == RiskLevel.Medium ? "  中风险" : "");
                string skipped = result.SkippedTooNew > 0 ? $"，{result.SkippedTooNew} 个太新跳过" : "";
                output.WriteLine($"  {rule.Name} ({rule.Id})：{result.Files} 个文件，{ByteSize.Format(result.Bytes)}{skipped}{flags}");
                if (!string.IsNullOrEmpty(rule.Note))
                {
                    output.WriteLine($"      提示：{rule.Note}");
                }

                foreach (var warning in result.Warnings)
                {
                    string where = warning.Target is { } target ? $"目标 {target}" : "回收站";
                    output.WriteLine($"      警告 {warning.Code}（{where}）：{warning.Detail}");
                }

                if (listPaths)
                {
                    foreach (var file in result.FileList)
                    {
                        output.WriteLine($"      {file.Path}");
                    }
                }
            }
        }

        output.WriteLine($"合计：{report.TotalFiles} 个文件，{ByteSize.Format(report.TotalBytes)}（{report.TotalBytes} 字节）");
    }

    public static void WriteClean(TextWriter output, CleanReport report)
    {
        foreach (var result in report.Rules)
        {
            output.WriteLine($"{result.Rule.Name} ({result.Rule.Id})：删除 {result.DeletedFiles} 个文件，{ByteSize.Format(result.DeletedBytes)}；失败 {result.Failed.Count} 个");
            foreach (var failure in result.Failed)
            {
                output.WriteLine($"    {failure.Error}  {failure.Path}");
            }
        }

        output.WriteLine($"合计删除：{report.TotalDeletedFiles} 个文件，{ByteSize.Format(report.TotalDeletedBytes)}（{report.TotalDeletedBytes} 字节）");
    }
}
