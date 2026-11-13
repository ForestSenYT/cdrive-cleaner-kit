using System.Globalization;

namespace CDriveCleaner.Core.Text;

public static class ByteSize
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    /// <summary>Human-readable size with binary units, e.g. "1.5 GB".</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {Units[unit]}");
    }
}
