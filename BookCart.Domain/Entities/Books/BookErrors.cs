using BookCart.Domain.Common.Results.Errors;

namespace BookCart.Domain.Entities.Books;

public static class BookErrors
{
    public static class Id
    {
        public static readonly Error Missing = Error.Validation(
            code: "Book.Id.Missing",
            description: "A BookId was expected but none was provided."
        );
        public static readonly Error InvalidFormat = Error.Validation(
            code: "Book.Id.InvalidFormat",
            description: $"Book Id is Invalid."
        );
    }
}
