using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Common.Abstractions.Errors;

//! Cross-cutting errors for the (state transitions) [[AMasterEntity]] implements for EVERY entity.
//! They are generic (apply to any entity), so they live WITH the base — not inside one aggregate's
//! catalog (CategoryErrors, BookErrors, ...). Conflict = the request fights the current state (→ 409).
public static class EntityStateErrors
{
    public static readonly Error AlreadyActive = Error.Conflict(
        "Entity.AlreadyActive",
        "The entity is already active."
    );

    public static readonly Error AlreadyInactive = Error.Conflict(
        "Entity.AlreadyInactive",
        "The entity is already inactive."
    );

    public static readonly Error AlreadyDeleted = Error.Conflict(
        "Entity.AlreadyDeleted",
        "The entity is already deleted."
    );

    /*
        //! NOT a readonly field like the others — the Code must carry WHICH aggregate was missing,
        //! so it stays greppable and keeps the "Aggregate.Reason" convention: "Category.NotFound".
        //?     - Callers pass typeof(TEntity).Name — that is how a GENERIC base repository produces an
        //?       aggregate-specific code without ever referencing CategoryErrors / BookErrors.
        //!     - The Description deliberately omits the id: never echo untrusted input back to the client.
    */
    public static Error NotFound(string entityName) =>
        Error.NotFound($"{entityName}.NotFound", $"The requested {entityName} was not found.");
}
