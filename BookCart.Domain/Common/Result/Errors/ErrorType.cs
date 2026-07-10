namespace BookCart.Domain.Common.Result.Errors;

//! 📖 §27 ErrorType - classification & HTTP mapping — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d403680d58118cc9c25fc551b
public enum ErrorType
{
    /// <summary>General, unclassified failure — use when no finer category applies. Also <c>default(ErrorType)</c>, deliberately the harmless one.</summary>
    Failure = 0,

    /// <summary>One or more inputs failed business-rule or format validation.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist.</summary>
    NotFound = 2,

    /// <summary>The operation would violate a uniqueness constraint or state invariant.</summary>
    Conflict = 3,

    /// <summary>The caller is not authenticated (no valid identity).</summary>
    Unauthorized = 4,

    /// <summary>The caller is authenticated but lacks the required permissions.</summary>
    Forbidden = 5,

    /// <summary>An unexpected condition occurred that should alert on-call (bugs, infra faults).</summary>
    Unexpected = 6,
}
