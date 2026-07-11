using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Entities.Categories;

/*
 //! The Category aggregate's own error catalog.
 *
 //?    - DDD principle: each aggregate OWNS the errors it can produce. Handlers, validators and
 //?    the API layer reference these stable Codes instead of inventing ad-hoc strings — so an
 //?    error's meaning has exactly one definition, and Code is greppable across the codebase.
 */
public static class CategoryErrors
{
    public static class Id
    {
        //? External input carried no id where one was required (null / empty / whitespace).
        public static readonly Error Missing = Error.Validation(
            "Category.Id.Missing",
            "A Category Id was expected but none was provided."
        );

        //? A value was supplied, but it is not a Category id (wrong / missing prefix).
        public static readonly Error InvalidFormat = Error.Validation(
            "Category.Id.InvalidFormat",
            $"Category Id is Invalid."
        //$"A Category Id must start with '{CategoryId.Prefix}'." //! For Security, don't reveal the Invalid Format Reason to the client. Just say it's invalid.
        );
    }
}
