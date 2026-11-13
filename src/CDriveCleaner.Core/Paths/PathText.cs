namespace CDriveCleaner.Core.Paths;

/// <summary>String-level path helpers. Internal paths are always absolute <c>X:\...</c> strings with backslashes.</summary>
internal static class PathText
{
    public const char Separator = '\\';
    private const string ExtendedPrefix = @"\\?\";

    private static readonly char[] InvalidNameCharacters = Path.GetInvalidFileNameChars();

    /// <summary>'/' becomes '\' and trailing separators are dropped, so <c>C:/x/</c> and <c>C:\x</c> read the same.</summary>
    public static string Normalize(string path) => path.Replace('/', Separator).TrimEnd(Separator);

    /// <summary>Splits a rule path into segments: '/' and '\' are equivalent, trailing separators are ignored.</summary>
    public static string[] Segments(string text) => Normalize(text).Split(Separator);

    /// <summary>Empty, "." and ".." segments are never allowed (SPEC sections 2 and 3).</summary>
    public static bool IsBadSegment(string segment) => segment is "" or "." or "..";

    /// <summary>A drive segment such as <c>C:</c>.</summary>
    public static bool IsDriveSegment(string segment) =>
        segment.Length == 2 && char.IsAsciiLetter(segment[0]) && segment[1] == ':';

    /// <summary>True when no file or directory can carry this name (wildcards, control characters, ':' and so on).</summary>
    public static bool CannotBeAName(string segment) => segment.AsSpan().IndexOfAny(InvalidNameCharacters) >= 0;

    public static string Join(string directory, string name) =>
        directory.EndsWith(Separator) ? directory + name : directory + Separator + name;

    /// <summary>
    /// Extended-length form used for every operating-system call. Besides lifting the length limit it turns off
    /// Win32 name normalization, so a file really named "a." can never be confused with a file named "a".
    /// </summary>
    public static string Extended(string path) =>
        path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? path : ExtendedPrefix + path;

    /// <summary>The plain <c>X:\...</c> form of a path an operating-system call returned in extended-length form.</summary>
    public static string WithoutExtendedPrefix(string path) =>
        path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? path[ExtendedPrefix.Length..] : path;
}
