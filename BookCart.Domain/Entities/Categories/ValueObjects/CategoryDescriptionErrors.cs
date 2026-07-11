using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Entities.Categories.ValueObjects;

public static class CategoryDescriptionErrors
{
    public static readonly Error CategoryDescriptionIsRequired = Error.NullValue(
        "CategoryDescription"
    );

    public static Error CategoryDescriptionIsTooLong(int maxDescriptionLength) =>
        Error.Validation(
            code: "CategoryDescription.TooLong",
            description: $"Category description must not exceed {maxDescriptionLength} characters."
        );
}
