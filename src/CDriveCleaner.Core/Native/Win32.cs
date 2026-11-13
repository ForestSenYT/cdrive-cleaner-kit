using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CDriveCleaner.Core.Native;

/// <summary>The handful of Win32 calls SPEC sections 6 and 7 name explicitly. Paths passed here are extended-length.</summary>
internal static partial class Win32
{
    public const uint GenericRead = 0x80000000;
    public const uint FileShareRead = 0x00000001;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x00000080;

    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidName = 123;

    public const uint SherbNoConfirmation = 0x00000001;
    public const uint SherbNoProgressUi = 0x00000002;
    public const uint SherbNoSound = 0x00000004;

    private const int GetFileExInfoStandard = 0;
    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly DateTime ToUtc() => DateTime.FromFileTimeUtc(((long)High << 32) | Low);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileAttributeData
    {
        public uint Attributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint SizeHigh;
        public uint SizeLow;

        public readonly long Size => ((long)SizeHigh << 32) | SizeLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FindData
    {
        private const int MaxPath = 260;

        public uint Attributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public fixed char FileName[MaxPath];
        public fixed char AlternateFileName[14];

        public readonly long Size => ((long)SizeHigh << 32) | SizeLow;

        public string Name
        {
            get
            {
                fixed (char* start = FileName)
                {
                    var buffer = new ReadOnlySpan<char>(start, MaxPath);
                    int end = buffer.IndexOf('\0');
                    return new string(end < 0 ? buffer : buffer[..end]);
                }
            }
        }
    }

    /// <summary>shellapi.h packs SHQUERYRBINFO to 1 byte only in 32-bit builds; this program is x64-only, so it is 24 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShQueryRecycleBinInfo
    {
        public uint CbSize;
        public long Size;
        public long NumItems;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteFile(string fileName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileAttributesEx(string fileName, int infoLevel, out FileAttributeData data);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindFirstFileEx(
        string fileName, int infoLevel, out FindData data, int searchOp, IntPtr searchFilter, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetLongPathName(string shortPath, [Out] char[] longPath, uint bufferLength);

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ShQueryRecycleBin(string rootPath, ref ShQueryRecycleBinInfo info);

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ShEmptyRecycleBin(IntPtr window, string rootPath, uint flags);

    /// <summary>Attributes of the path itself; a reparse point is reported, never followed. Returns the Win32 error, 0 on success.</summary>
    public static int TryGetAttributes(string path, out FileAttributeData data)
    {
        return GetFileAttributesEx(path, GetFileExInfoStandard, out data) ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>The directory entry for one exact name (no wildcard in it), as the parent directory lists it.</summary>
    public static int TryFindEntry(string path, out FindData data)
    {
        IntPtr handle = FindFirstFileEx(path, FindExInfoBasic, out data, FindExSearchNameMatch, IntPtr.Zero, 0);
        if (handle == InvalidHandleValue)
        {
            return Marshal.GetLastPInvokeError();
        }

        FindClose(handle);
        return 0;
    }

    /// <summary>Long form of an existing path (8.3 aliases expanded), or null when it cannot be resolved.</summary>
    public static string? LongPath(string path)
    {
        var buffer = new char[1024];
        uint length = GetLongPathName(path, buffer, (uint)buffer.Length);
        if (length == 0)
        {
            return null;
        }

        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = GetLongPathName(path, buffer, (uint)buffer.Length);
            if (length == 0 || length > buffer.Length)
            {
                return null;
            }
        }

        return new string(buffer, 0, (int)length);
    }
}
