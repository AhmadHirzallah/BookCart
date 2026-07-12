using BookCart.Domain.Entities.Categories;
using BookCart.Domain.Entities.Categories.ValueObjects;

namespace BookCart.Application.Common.Abstractions.Data.Repositories;

public interface ICategoryRepository : IBaseRepository<Category, CategoryId>
{
    //! I can add more methods specific to the [[Category repository]] here, if needed.
    Task<bool> ExistsWithNameAsync(CategoryName categoryName, CancellationToken ct = default);
    Task<Category?> GetByNameAsync(CategoryName categoryName, CancellationToken ct = default);
}
