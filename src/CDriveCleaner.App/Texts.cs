using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.App;

/// <summary>User-facing wording for codes defined by the specification.</summary>
internal static class Texts
{
    public const string AppTitle = "C 盘清理";

    public static readonly Color MediumRiskColor = Color.FromArgb(196, 89, 0);

    public static string Risk(RiskLevel risk) => risk == RiskLevel.Medium ? "中" : "低";

    public static string Warning(string code) => code switch
    {
        WarningCodes.UnresolvedVariable => "环境变量未定义，已跳过该目标",
        WarningCodes.InvalidRoot => "根路径无效，已跳过该目标",
        WarningCodes.ReparsePointInRoot => "根路径经过链接或联接点，已跳过",
        WarningCodes.ProtectedRoot => "受保护目录，不做整体清理",
        WarningCodes.RecycleBinQueryFailed => "无法查询回收站",
        _ => code,
    };

    public static string Error(string code) => code switch
    {
        CleanErrorCodes.Gone => "文件已不存在",
        CleanErrorCodes.NotRegularFile => "不是普通文件",
        CleanErrorCodes.InUseOrDenied => "正被占用或没有权限",
        CleanErrorCodes.DeleteFailed => "删除失败（只读或权限不足）",
        _ => code,
    };
}
