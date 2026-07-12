using BookCart.Domain.Common.Abstractions;

namespace BookCart.Domain.Entities.Books.ValueObjects;

public sealed record Isbn : ASingleValueObject<string> { }
