using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Entities.Categories.ValueObjects;

public static class CategoryNameErrors
{
    public static readonly Error CategoryNameIsRequired = Error.NullValue("CategoryName");

    public static Error CategoryNameIsTooLong(int maxNameLength) =>
        Error.Validation(
            code: "CategoryName.TooLong",
            description: $"Category name must not exceed {maxNameLength} characters."
        );
}
