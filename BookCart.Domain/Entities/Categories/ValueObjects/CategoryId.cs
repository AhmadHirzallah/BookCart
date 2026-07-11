using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Results;

namespace BookCart.Domain.Entities.Categories.ValueObjects;

/*
 //!? The strongly-typed, lexicographically-sortable identity of a [[Category]] aggregate.
 //!? Format: "category-{UUIDv7}".
 //>       New()  → generates a guaranteed-valid [[id]]; no validation needed.
 //>       From() → reconstitutes (يعيد تكوين) an UNTRUSTED [[string]], mapping each [failure reason] to THIS aggregate's errors (CategoryErrors.Id.*). Returns Result<T>.
 //* Value + ToString() are inherited — never restated. That is the redundancy, gone.
*/
//! My Notes About: "Strongly-Typed IDs & the Abstract Base Pattern": https://app.notion.com/p/Strongly-Typed-IDs-the-Abstract-Base-Pattern-A-Complete-Guide-39a0535d40368013bfdafcfb23199888
public sealed record CategoryId : AStronglyTypedId
{
    #region Constants

    //? The self-describing prefix. Public so CategoryErrors / tests can reference it.
    public const string Prefix = "category-";

    #endregion


    #region Constructor

    //! private: force every instance through New() or From().
    private CategoryId(string value)
        : base(value) { }

    #endregion


    #region Factories

    //? Generates a fresh, guaranteed-valid [CategoryId]. Used when creating a NEW Category.
    public static CategoryId New() => new(NewValue(Prefix));

    /*
     //!    - Reconstitutes a [CategoryId] from an externally supplied [[string]] (API route param, message payload, ...).
     //?    - Used inside Update/Delete handlers where the boundary hands us a primitive string, but the domain works with [CategoryId].
     //* Base CheckFormat() reports the *reason* it failed; we map that reason to OUR errors.
     //>    - The switch arms are target-typed to Result<CategoryId>:
     //>        - CategoryId          → implicit operator Result<CategoryId>(value)  (success)
     //>        - CategoryErrors.Id.* → implicit operator Result<CategoryId>(error)  (failure)
    */
    public static Result<CategoryId> From(string? value) =>
        CheckFormat(value, Prefix) switch
        {
            FormatCheck.Valid => new CategoryId(value!),
            FormatCheck.Missing => CategoryErrors.Id.Missing,
            _ => CategoryErrors.Id.InvalidFormat,
        };

    #endregion
}
