using BookCart.Domain.Common.Abstractions;

namespace BookCart.Application.Common.Abstractions.Data.Repositories;

//! To [[encapsulate]] the logic required to access data sources, allowing my [[Application/Domain]] logic to remain completely ignorant of data accessing/storing concerns or whether you are using [SQL Server] or any other databases.
//! Check my Notes of "The Repository Pattern & Aggregate Roots & Unit of Work": https://app.notion.com/p/The-Repository-Pattern-only-with-aggregate-roots-Unit-of-Work-A-Complete-Mastering-Guide-Book-39a0535d4036806c92f8f5ba643d5d65
public interface IBaseRepository<TEntity, TIdKeyType>
    where TEntity : ABaseEntity<TIdKeyType>, IAggregateRoot
    where TIdKeyType : notnull
{
    #region Reading (Queries)

    Task<TEntity?> GetByIdAsync(TIdKeyType id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> DoesItExistAsync(TIdKeyType id, CancellationToken ct = default);

    #endregion Reading

    #region Writing (Commands)

    Task AddAsync(TEntity entity, CancellationToken ct);
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    //! //! [Hard Delete]; For [Soft Delete] use: [AMasterEntity.MarkAsDeleted()]
    Task DeleteAsync(TEntity entity, CancellationToken ct = default);
    #endregion Writing
}
