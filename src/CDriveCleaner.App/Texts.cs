using CDriveCleaner.Core.Cleaning;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.App;

/// <summary>User-facing wording for codes defined by the specification.</summary>
internal static class Texts
{
    public const string AppTitle = "C 盘清理";

    public static string Risk(RiskLevel risk) => risk == RiskLevel.Medium ? "中风险" : "低风险";

    public static string Warning(string code) => code switch
    {
        WarningCodes.UnresolvedVariable => "环境变量未定义，已跳过该位置",
        WarningCodes.InvalidRoot => "路径无效，已跳过该位置",
        WarningCodes.ReparsePointInRoot => "路径经过链接或联接点，已跳过",
        WarningCodes.ProtectedRoot => "受保护的系统文件夹，不做整体清理",
        WarningCodes.RecycleBinQueryFailed => "无法查询回收站",
        _ => code,
    };

    public static string Error(string code) => code switch
    {
        CleanErrorCodes.Gone => "文件已不存在",
        CleanErrorCodes.NotRegularFile => "不是普通文件",
        CleanErrorCodes.InUseOrDenied => "正在使用或没有权限",
        CleanErrorCodes.DeleteFailed => "删除失败（只读或权限不足）",
        _ => code,
    };

    /// <summary>"3 天前", "5 小时前": how long ago a file was last changed.</summary>
    public static string Age(DateTime utc)
    {
        if (utc == DateTime.MinValue)
        {
            return "";
        }

        var age = DateTime.UtcNow - utc;
        return age.TotalDays >= 365 ? $"{(int)(age.TotalDays / 365)} 年前"
            : age.TotalDays >= 30 ? $"{(int)(age.TotalDays / 30)} 个月前"
            : age.TotalDays >= 1 ? $"{(int)age.TotalDays} 天前"
            : age.TotalHours >= 1 ? $"{(int)age.TotalHours} 小时前"
            : "刚刚";
    }
}
