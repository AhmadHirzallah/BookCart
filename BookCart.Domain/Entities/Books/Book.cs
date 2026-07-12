using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Entities.Books.ValueObjects;
using BookCart.Domain.Entities.Categories;
using BookCart.Domain.Entities.Categories.ValueObjects;

namespace BookCart.Domain.Entities.Books;

public sealed class Book : AMasterEntity<BookId>, IAggregateRoot
{
    public Isbn Isbn { get; private set; }

    #region For EF Navigation

    //TODO: Check the (set;) modifier for the [[Category]] property. Should I make it [[private set]] ? or [[protected set]] ?? to prevent external modification.
    public Category Category { get; set; } = default!;

    //TODO: Check the (set;) modifier for the [[CategoryId]] property. Should I make it [[private set]] ? or [[protected set]] ?? to prevent external modification.
    public CategoryId CategoryId { get; private set; }

    #endregion
}
