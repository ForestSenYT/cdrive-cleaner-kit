namespace CDriveCleaner.Core.Insights;

/// <param name="Name">The program, as people know it.</param>
/// <param name="MayHoldPersonalFiles">
/// False for installers and build tools: their pictures and archives are their own payload, not something a person
/// saved, so they are not pointed out as possibly useful.
/// </param>
public sealed record Owner(string Name, bool MayHoldPersonalFiles);

/// <summary>
/// Recognises folders that well-known programs leave in temp and cache locations, from the folder's name or the
/// names of the files inside it. A guess, used only to label the breakdown; it never decides what is deleted.
/// </summary>
internal static class Owners
{
    public static readonly Owner VisualStudioInstaller = new("Visual Studio 安装程序", false);
    public static readonly Owner VisualStudioBuild = new("Visual Studio 编译临时文件", false);
    public static readonly Owner DotNetPackages = new(".NET 安装包缓存", false);
    public static readonly Owner DotNetSingleFile = new(".NET 程序解压目录", false);
    public static readonly Owner VsCodeUpdate = new("VS Code 更新程序", false);
    public static readonly Owner Autodesk = new("Autodesk 安装程序", false);
    public static readonly Owner ExtractedInstaller = new("软件安装包解压目录", false);
    public static readonly Owner PyInstaller = new("Python 打包程序解压目录", false);
    public static readonly Owner Pip = new("Python 包安装临时文件", false);
    public static readonly Owner WinGet = new("WinGet 安装包", false);
    public static readonly Owner Chocolatey = new("Chocolatey 安装缓存", false);
    public static readonly Owner ClaudeCode = new("Claude Code 临时文件", true);

    /// <param name="folderName">Name of the folder directly under the scanned root.</param>
    /// <param name="filePath">One file inside that folder.</param>
    public static Owner? Recognise(string folderName, string filePath)
    {
        if (ByFolder(folderName) is { } owner)
        {
            return owner;
        }

        string fileName = Path.GetFileName(filePath);
        if (fileName.Equals("InstallCleanup.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("vs_installer", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("vs_setup_bootstrapper", StringComparison.OrdinalIgnoreCase))
        {
            return VisualStudioInstaller;
        }

        string extension = Path.GetExtension(fileName);
        bool package = extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cab", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msp", StringComparison.OrdinalIgnoreCase);
        if (package && folderName.StartsWith('{') && folderName.EndsWith('}'))
        {
            return ExtractedInstaller;
        }

        if ((package || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
            && filePath.Contains(@"\Microsoft.NET.", StringComparison.OrdinalIgnoreCase))
        {
            return DotNetPackages;
        }

        return null;
    }

    private static Owner? ByFolder(string name)
    {
        if (name.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeCode;
        }

        if (name.Equals("Roslyn", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VBCSCompiler", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("MSBuildTemp", StringComparison.OrdinalIgnoreCase))
        {
            return VisualStudioBuild;
        }

        if (name.Equals(".net", StringComparison.OrdinalIgnoreCase))
        {
            return DotNetSingleFile;
        }

        if (name.StartsWith("vscode-", StringComparison.OrdinalIgnoreCase))
        {
            return VsCodeUpdate;
        }

        if (name.Equals("odis_download_dest", StringComparison.OrdinalIgnoreCase))
        {
            return Autodesk;
        }

        if (name.StartsWith("_MEI", StringComparison.Ordinal))
        {
            return PyInstaller;
        }

        if (name.StartsWith("pip-", StringComparison.OrdinalIgnoreCase))
        {
            return Pip;
        }

        if (name.Equals("WinGet", StringComparison.OrdinalIgnoreCase))
        {
            return WinGet;
        }

        if (name.Equals("chocolatey", StringComparison.OrdinalIgnoreCase))
        {
            return Chocolatey;
        }

        return null;
    }
}
