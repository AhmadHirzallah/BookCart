using BookCart.Application.Common.Abstractions.Data.Repositories;
using BookCart.Domain.Entities.Books;
using BookCart.Domain.Entities.Books.ValueObjects;
using BookCart.Domain.Entities.Categories.ValueObjects;
using BookCart.Infrastructure.Persistence.DbContexts;

namespace BookCart.Infrastructure.Persistence.Repositories;

internal sealed class BookRepository : ABaseRepository<Book, BookId>, IBookRepository
{
    #region Construction

    public BookRepository(BookCartDbContext dbContext)
        : base(dbContext) { }

    #endregion

    #region Overrides [[TODO]]:

    //protected override IQueryable<Book> IncludeAggregate(IQueryable<Book> query) =>
    //    query.Include(b => b.Category).Include(b => b.Author).Include(b => b.Reviews);

    #endregion


    #region Implementations (of [[IBookRepository]] Contract)

    public async Task<IReadOnlyList<Book>> GetByCategoryAsync(
        CategoryId categoryId,
        CancellationToken ct = default
    ) => await FindUsingPredicateAsync(b => b.CategoryId.Equals(categoryId), ct: ct);

    public Task<bool> IsIsbnTakenAsync(Isbn isbn, CancellationToken ct = default) =>
        AnyAsync(b => b.Isbn.Equals(isbn), ct: ct);

    #endregion
}
