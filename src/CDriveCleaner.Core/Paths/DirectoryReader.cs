using System.IO.Enumeration;
using CDriveCleaner.Core.Native;

namespace CDriveCleaner.Core.Paths;

internal readonly record struct DirectoryEntry(string Name, FileAttributes Attributes, long Length, DateTime LastWriteUtc)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
}

/// <summary>
/// Directory enumeration only (SPEC section 10): attributes, size and modification time come straight from the
/// listing, and no file is opened. Unreadable directories read as empty.
/// </summary>
internal static class DirectoryReader
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static List<DirectoryEntry> List(string directory)
    {
        try
        {
            var entries = new FileSystemEnumerable<DirectoryEntry>(
                PathText.Extended(directory),
                static (ref FileSystemEntry entry) => new DirectoryEntry(
                    entry.FileName.ToString(), entry.Attributes, entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                Options);
            return [.. entries];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Looks up one literal child name. The entry comes back with its on-disk spelling, so an 8.3 alias or a
    /// different letter case resolves to the real long name. Returns null when nothing by that name exists.
    /// </summary>
    public static DirectoryEntry? Find(string directory, string name)
    {
        if (PathText.CannotBeAName(name))
        {
            return null; // also keeps '?', '<', '>' and '"' from acting as FindFirstFileEx wildcards
        }

        string path = PathText.Join(directory, name);
        int error = Win32.TryFindEntry(PathText.Extended(path), out var found);
        if (error == 0)
        {
            return new DirectoryEntry(found.Name, (FileAttributes)found.Attributes, found.Size, found.LastWriteTime.ToUtc());
        }

        if (error != Win32.ErrorAccessDenied)
        {
            return null;
        }

        // The parent cannot be listed but the entry itself may still be visible: fall back to its own attributes.
        if (Win32.TryGetAttributes(PathText.Extended(path), out var data) != 0)
        {
            return null;
        }

        return new DirectoryEntry(name, (FileAttributes)data.Attributes, data.Size, data.LastWriteTime.ToUtc());
    }
}
