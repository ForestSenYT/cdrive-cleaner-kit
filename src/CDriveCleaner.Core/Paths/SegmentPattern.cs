namespace CDriveCleaner.Core.Paths;

/// <summary>
/// One path segment of a rule pattern (SPEC section 3). The only wildcard is '*', which matches any run of
/// characters (including none) inside a single segment. Every other character is literal; comparison ignores case.
/// </summary>
internal sealed class SegmentPattern
{
    private readonly string _text;
    private readonly string[]? _parts;

    private SegmentPattern(string text)
    {
        _text = text;
        _parts = text.Contains('*') ? text.Split('*') : null;
        IsAllStars = text.Length > 0 && text.AsSpan().TrimStart('*').IsEmpty;
    }

    /// <summary>The segment consists of nothing but '*' characters.</summary>
    public bool IsAllStars { get; }

    public string Text => _text;

    public static SegmentPattern Parse(string segment) => new(segment);

    public static SegmentPattern[] ParsePath(string pattern) =>
        Array.ConvertAll(PathText.Segments(pattern), Parse);

    public bool Matches(string name)
    {
        if (_parts is null)
        {
            return string.Equals(name, _text, StringComparison.OrdinalIgnoreCase);
        }

        // Ordinal ignore-case comparison maps character by character, so lengths below are exact.
        string first = _parts[0];
        string last = _parts[^1];
        if (name.Length < first.Length + last.Length
            || !name.StartsWith(first, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(last, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int position = first.Length;
        int end = name.Length - last.Length;
        for (int index = 1; index < _parts.Length - 1; index++)
        {
            string part = _parts[index];
            if (part.Length == 0)
            {
                continue;
            }

            int found = name.IndexOf(part, position, end - position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + part.Length;
        }

        return true;
    }

    public override string ToString() => _text;
}
