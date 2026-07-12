using BookCart.Domain.Entities.Books;
using BookCart.Domain.Entities.Books.ValueObjects;
using BookCart.Domain.Entities.Categories.ValueObjects;

namespace BookCart.Application.Common.Abstractions.Data.Repositories;

public interface IBookRepository : IBaseRepository<Book, BookId>
{
    //! You can add more methods specific to the [[Book repository]] here, if needed.
    Task<bool> IsIsbnTakenAsync(Isbn isbn, CancellationToken ct = default);
    Task<IReadOnlyList<Book>> GetByCategoryAsync(
        CategoryId categoryId,
        CancellationToken ct = default
    );
}
