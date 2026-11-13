using System.Diagnostics;
using CDriveCleaner.Core.Native;
using CDriveCleaner.Core.Paths;
using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;

namespace CDriveCleaner.Core.Cleaning;

/// <summary>
/// Permanently deletes the files a scan listed for the selected rules (SPEC section 7). Rules are not matched again.
/// Every file is re-checked right before deletion. No directory is removed, no attribute changed, nothing retried
/// or scheduled for reboot.
/// </summary>
public sealed class Cleaner
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <param name="selectedIds">Ids of rules the user selected and confirmed; every id must be in <paramref name="scan"/>.</param>
    public CleanReport Clean(
        ScanReport scan, IReadOnlySet<string> selectedIds, IProgress<CleanProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        var selected = scan.Rules.Where(result => selectedIds.Contains(result.Rule.Id)).ToList();
        if (selected.Count != selectedIds.Count)
        {
            throw new ArgumentException("所选规则不在本次扫描结果中。", nameof(selectedIds));
        }

        long total = selected.Where(r => r.Rule.Kind == RuleKind.Files).Sum(r => (long)r.FileList.Count);
        long processed = 0;
        var clock = Stopwatch.StartNew();
        var results = new List<RuleCleanResult>(selected.Count);
        bool cancelled = false;

        foreach (var scanned in selected)
        {
            if (cancellation.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var result = new RuleCleanResult(scanned.Rule);
            results.Add(result);
            progress?.Report(new CleanProgress(processed, total, scanned.Rule));

            if (scanned.Rule.Kind == RuleKind.RecycleBin)
            {
                EmptyRecycleBin(scanned, result);
                continue;
            }

            foreach (var file in scanned.FileList)
            {
                if (cancellation.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                string? error = DeleteOne(file.Path, out long size);
                if (error is null)
                {
                    result.DeletedFiles++;
                    result.DeletedBytes += size;
                }
                else
                {
                    result.Failed.Add(new CleanFailure(file.Path, error));
                }

                processed++;
                if (progress is not null && clock.Elapsed >= ProgressInterval)
                {
                    progress.Report(new CleanProgress(processed, total, scanned.Rule));
                    clock.Restart();
                }
            }

            if (cancelled)
            {
                break;
            }
        }

        if (progress is not null && results.Count > 0)
        {
            progress.Report(new CleanProgress(processed, total, results[^1].Rule));
        }

        return new CleanReport(results, cancelled);
    }

    /// <summary>
    /// Re-checks one file and deletes it. Returns null on success, otherwise the error code of the first check
    /// that failed. <paramref name="size"/> is the file's size as found right before deletion.
    /// </summary>
    internal static string? DeleteOne(string path, out long size)
    {
        size = 0;
        string system = PathText.Extended(path);

        int error = Win32.TryGetAttributes(system, out var data);
        if (error != 0)
        {
            // The file exists but its attributes cannot be read: same outcome as a failed probe.
            return error is Win32.ErrorFileNotFound or Win32.ErrorPathNotFound or Win32.ErrorInvalidName
                ? CleanErrorCodes.Gone
                : CleanErrorCodes.InUseOrDenied;
        }

        var attributes = (FileAttributes)data.Attributes;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            return CleanErrorCodes.NotRegularFile;
        }

        if (!CanOpenSharedRead(system))
        {
            return CleanErrorCodes.InUseOrDenied;
        }

        size = data.Size;
        if (!Win32.DeleteFile(system))
        {
            size = 0;
            return CleanErrorCodes.DeleteFailed;
        }

        return null;
    }

    /// <summary>
    /// In-use probe: read-only open that shares read access only. It fails while any other handle has the file
    /// open for writing (even one that shares delete), and when this account may not read it.
    /// </summary>
    private static bool CanOpenSharedRead(string systemPath)
    {
        using var handle = Win32.CreateFile(
            systemPath, Win32.GenericRead, Win32.FileShareRead, IntPtr.Zero, Win32.OpenExisting, Win32.FileAttributeNormal,
            IntPtr.Zero);
        return !handle.IsInvalid;
    }

    /// <summary>
    /// SHEmptyRecycleBinW on the system drive. The report carries the item count and size queried before emptying.
    /// SPEC section 6 does not define a failure code for this call; a failure is reported as delete_failed on the drive root.
    /// </summary>
    private static void EmptyRecycleBin(RuleScanResult scanned, RuleCleanResult result)
    {
        bool knownEmpty = scanned.Files == 0 && scanned.Bytes == 0 && scanned.Warnings.Count == 0;
        if (knownEmpty)
        {
            return;
        }

        if (RecycleBin.Empty() != 0)
        {
            result.Failed.Add(new CleanFailure(RecycleBin.SystemDriveRoot, CleanErrorCodes.DeleteFailed));
            return;
        }

        result.DeletedFiles = scanned.Files;
        result.DeletedBytes = scanned.Bytes;
    }
}
