namespace BookCart.Application.Common.Abstractions.Data.Repositories;

//! Check my Notes of "The Repository Pattern & Aggregate Roots & Unit of Work": https://app.notion.com/p/The-Repository-Pattern-only-with-aggregate-roots-Unit-of-Work-A-Complete-Mastering-Guide-Book-39a0535d4036806c92f8f5ba643d5d65
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
