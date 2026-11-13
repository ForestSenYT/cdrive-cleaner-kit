using CDriveCleaner.Core.Native;
using CDriveCleaner.Core.Paths;

namespace CDriveCleaner.Core.Scanning;

/// <summary>
/// Directories that must never be cleaned as a whole (SPEC section 4): every drive root; %SystemRoot% and its
/// System32, SysWOW64 and WinSxS; the program and profile directories. Comparison ignores case and trailing separators.
/// </summary>
internal sealed class ProtectedDirectories
{
    private static readonly string[] Variables =
        ["SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "USERPROFILE", "APPDATA", "LOCALAPPDATA"];

    private static readonly string[] SystemSubdirectories = ["System32", "SysWOW64", "WinSxS"];

    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public static ProtectedDirectories FromEnvironment()
    {
        var result = new ProtectedDirectories();
        foreach (string variable in Variables)
        {
            result.Add(Environment.GetEnvironmentVariable(variable));
        }

        string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(systemRoot))
        {
            foreach (string name in SystemSubdirectories)
            {
                result.Add(PathText.Join(PathText.Normalize(systemRoot), name));
            }
        }

        string? systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        if (!string.IsNullOrEmpty(systemDrive))
        {
            result.Add(PathText.Normalize(systemDrive) + PathText.Separator + "Users");
        }

        return result;
    }

    public bool Contains(string path)
    {
        string normalized = PathText.Normalize(path);
        return PathText.IsDriveSegment(normalized) || _paths.Contains(normalized);
    }

    private void Add(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string normalized = PathText.Normalize(path);
        _paths.Add(normalized);

        // Scanned paths always carry long names; make an 8.3-style variable value compare equal to them too.
        if (Win32.LongPath(PathText.Extended(normalized)) is { } longPath)
        {
            _paths.Add(PathText.Normalize(PathText.WithoutExtendedPrefix(longPath)));
        }
    }
}
