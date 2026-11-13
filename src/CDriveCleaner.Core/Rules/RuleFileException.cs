namespace CDriveCleaner.Core.Rules;

/// <summary>The rule file is rejected as a whole (SPEC section 2; command line exit code 2).</summary>
public sealed class RuleFileException : Exception
{
    public RuleFileException(string message)
        : base(message)
    {
    }

    public RuleFileException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
