using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Results;

namespace BookCart.Domain.Entities.Books.ValueObjects;

public sealed record BookId : AStronglyTypedId
{
    #region Constants

    //? The self-describing prefix. Public so CategoryErrors / tests can reference it.
    public const string Prefix = "book-";

    #endregion

    #region Construction

    public BookId(string value)
        : base(value) { }

    #endregion

    #region Factories

    //? Generates a fresh, guaranteed-valid [BookId]. Used when creating a NEW Book.
    public static BookId New() => new(NewValue(Prefix));

    /*
     //!    - Reconstitutes a [BookId] from an externally supplied [[string]] (API route param, message payload, ...).
     //?    - Used inside Update/Delete handlers where the boundary hands us a primitive string, but the domain works with [BookId].
     //* Base CheckFormat() reports the *reason* it failed; we map that reason to OUR errors.
     //>    - The switch arms are target-typed to Result<BookId>:
     //>        - BookId          → implicit operator Result<BookId>(value)  (success)
     //>        - BookErrors.Id.* → implicit operator Result<BookId>(error)  (failure)
    */
    public static Result<BookId> From(string? value) =>
        CheckFormat(value, Prefix) switch
        {
            FormatCheck.Valid => new BookId(value!),
            FormatCheck.Missing => BookErrors.Id.Missing,
            _ => BookErrors.Id.InvalidFormat,
        };

    #endregion
}
