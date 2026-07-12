using BookCart.Application.Common.Abstractions.Data.Repositories;
using BookCart.Domain.Entities.Categories;
using BookCart.Domain.Entities.Categories.ValueObjects;
using BookCart.Infrastructure.Persistence.DbContexts;

namespace BookCart.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository
    : ABaseRepository<Category, CategoryId>,
        ICategoryRepository
{
    #region Construction

    public CategoryRepository(BookCartDbContext dbContext)
        : base(dbContext) { }

    #endregion

    #region Implementations (of ICategoryRepository Contract)

    public Task<bool> ExistsWithNameAsync(
        CategoryName categoryName,
        CancellationToken ct = default
    //) => AnyAsync(x => x.Name == categoryName, ct: ct);
    ) => AnyAsync(x => x.Name.Equals(categoryName), ct: ct);

    public Task<Category?> GetByNameAsync(
        CategoryName categoryName,
        CancellationToken ct = default
    ) => FindOneUsingPredicateAsync(x => x.Name.Equals(categoryName), tracked: true, ct: ct);

    #endregion Implementations (of Contract)
}
