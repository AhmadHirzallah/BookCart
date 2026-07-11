namespace BookCart.Domain.Common.Results.Errors;

//! 📖 §20 The Error struct — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d403680e98416fd1bf58fff53
public readonly record struct Error
{
    #region Properties

    /// <summary>Machine-readable, stable, greppable. Convention: <c>Aggregate.Reason</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable. May change freely — never branch on it.</summary>
    public string Description { get; }

    /// <summary>Classification other layers map to HTTP status, log level, or an alert.</summary>
    public ErrorType ErrorType { get; }

    //! 📖 §22 ToString() — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d40368082832ed3a85dffdc69
    public override string ToString()
    {
        return $"[[{ErrorType}] {Code}: {Description}]";
    }

    #endregion Properties


    #region Constructors( [private] & I use the Factory Pattern Methods )

    //! 📖 §23 The private constructor — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d403680a3ab17c4f30037d2f4
    private Error(string code, string description, ErrorType errorType)
    {
        Code = code;
        Description = description;
        ErrorType = errorType;
    }

    #endregion Constructors([private] & I use the Factory Pattern Methods)


    #region Result Pattern & [[ Factory Pattern Methods ]] - One Static Factory Method [[ PER [ErrorType]  ]] for a fluent, self-documenting API

    //! 📖 §24 Error.None — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d4036801c9695ca80dffc233b
    public static readonly Error None = new Error(string.Empty, string.Empty, ErrorType.Failure);

    //public static readonly Error NullValue = new(
    //    "Error.NullValue",
    //    "A null or missing value was provided where a non-null value is required.",
    //    ErrorType.Failure
    //);

    public static Error NullValue(string property) =>
        new(
            $"Error.{property}.NullValue",
            $"A null or missing value was provided for '{property}' where a non-null value is required.",
            ErrorType.Failure
        );

    //! 📖 §26 The typed factory methods — https://app.notion.com/p/Result-Pattern-Notes-3990535d403680109860fd410ff64c66?source=copy_link#3990535d40368062ae16f17145f1f587

    /// <summary>General, unclassified failure — when no finer category applies. → 400</summary>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    /// <summary>One or more inputs failed business-rule or format validation. → 422 (or 400)</summary>
    public static Error Validation(string code, string description) =>
        new Error(code, description, ErrorType.Validation);

    /// <summary>The requested resource does not exist. → 404</summary>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    /// <summary>The operation would violate a uniqueness constraint or state invariant. → 409</summary>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    /// <summary>The caller is not authenticated (no valid identity). → 401</summary>
    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    /// <summary>The caller is authenticated but lacks the required permissions. → 403</summary>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    /// <summary>An unexpected condition that should alert on-call (bugs, infra faults). → 500</summary>
    public static Error Unexpected(string code, string description) =>
        new Error(code, description, ErrorType.Unexpected);

    //! Factory method: IF you have an [[Error]] type that doesn't fit the above categories (Validation, Forbidden, etc.), you can use this method to create a custom [[Error]] with a specific [[ErrorType]].
    public static Error Create(string code, string description, ErrorType type) =>
        new(code, description, type);

    #endregion Factory Pattern Methods
}
