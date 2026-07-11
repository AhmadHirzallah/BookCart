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
}
