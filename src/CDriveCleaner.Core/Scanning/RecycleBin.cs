using System.Runtime.CompilerServices;
using CDriveCleaner.Core.Native;

namespace CDriveCleaner.Core.Scanning;

/// <summary>The system drive's recycle bin (SPEC section 6).</summary>
public static class RecycleBin
{
    /// <summary>Root directory of %SystemDrive%, for example <c>C:\</c>.</summary>
    public static string SystemDriveRoot
    {
        get
        {
            string? drive = Environment.GetEnvironmentVariable("SystemDrive");
            return (string.IsNullOrEmpty(drive) ? "C:" : drive.TrimEnd('\\', '/')) + @"\";
        }
    }

    /// <summary>Item count and total size, or null when SHQueryRecycleBinW fails.</summary>
    public static (long Items, long Bytes)? Query()
    {
        RequireSixtyFourBit();
        var info = new Win32.ShQueryRecycleBinInfo { CbSize = (uint)Unsafe.SizeOf<Win32.ShQueryRecycleBinInfo>() };
        int result = Win32.ShQueryRecycleBin(SystemDriveRoot, ref info);
        return result == 0 ? (info.NumItems, info.Size) : null;
    }

    /// <summary>Empties the system drive's recycle bin without any confirmation, progress or sound. Returns the HRESULT.</summary>
    public static int Empty()
    {
        RequireSixtyFourBit();
        return Win32.ShEmptyRecycleBin(
            IntPtr.Zero, SystemDriveRoot, Win32.SherbNoConfirmation | Win32.SherbNoProgressUi | Win32.SherbNoSound);
    }

    private static void RequireSixtyFourBit()
    {
        // In a 32-bit process the native structure would be the 20-byte packed form.
        if (!Environment.Is64BitProcess || Unsafe.SizeOf<Win32.ShQueryRecycleBinInfo>() != 24)
        {
            throw new PlatformNotSupportedException("回收站接口要求 64 位进程。");
        }
    }
}
