namespace BookCart.Domain.Common.Abstractions;

//! My Notes About: "Strongly-Typed IDs & the Abstract Base Pattern": https://app.notion.com/p/Strongly-Typed-IDs-the-Abstract-Base-Pattern-A-Complete-Guide-39a0535d40368013bfdafcfb23199888
public abstract record AStronglyTypedId
{
    //! Members:
    //!     - Value, ToString(), NewValue(prefix), CheckFormat(value, prefix)

    #region Properties (Value) + Construction

    //! The full identifier string, e.g. "category-019750ab-...".
    public string Value { get; }

    //! protected: an id can only be created through a concrete type's New()/From() factory.
    protected AStronglyTypedId(string value)
    {
        Value = value;
    }

    #endregion


    #region Shared Mechanics (reused by every concrete id)

    /*
        //?     - Builds a fresh value: "{prefix}{UUIDv7}".
        //?     - UUIDv7 embeds a millisecond timestamp, so ids sort by creation time with no separate CreatedAt column.
        //>     - [protected static] → callable only from a derived New().
    */
    protected static string NewValue(string prefix) =>
        string.Concat(prefix, Guid.CreateVersion7().ToString());

    /*
     //!    - Structural validation of an [[externally]] supplied string — WITHOUT knowing which Error type to raise.
     //?    - It reports the *reason*; the concrete From() maps that reason to its own aggregate error. This is what keeps the base free of CategoryErrors etc.
    */
    protected static FormatCheck CheckFormat(string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FormatCheck.Missing;
        }

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return FormatCheck.WrongPrefix;
        }

        return FormatCheck.Valid;
    }

    #endregion


    #region ToString

    /*
     //!    - [[sealed]] is load-bearing, not cosmetic => هو حامل للأحمال، وليس تجميليًا.
     //!    - Records [ public abstract record AStronglyTypedId ] => will auto-generate [ ToString() ] in EVERY record. Without 'sealed' here, each [[ derived [id] ]] would silently regenerate it and print "CategoryId { Value = ... }", overriding this one.
     //*    - 'sealed override' suppresses that regeneration → one [[ ToString ]]
     //>    - for the whole hierarchy: the [raw value], transparent in [logs] and [serialisation].
     */
    public sealed override string ToString() => Value;

    #endregion


    #region Nested Types

    //! The outcome of [[CheckFormat]] — a reason, not an [[error]]. The [[concrete id]] owns the mapping to errors.
    public enum FormatCheck
    {
        Valid,
        Missing,
        WrongPrefix,
    }

    #endregion
}
