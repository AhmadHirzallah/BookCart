using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Results;

namespace BookCart.Application.Common.Abstractions.Data.Repositories;

/*
    //* To [[encapsulate]] the logic required to access data sources, allowing my [[Application/Domain]] logic to remain completely ignorant of data accessing/storing concerns or whether you are using [SQL Server] or any other databases.
    //! 1) Check my Notes of "The Repository Pattern & Aggregate Roots & Unit of Work": https://app.notion.com/p/The-Repository-Pattern-only-with-aggregate-roots-Unit-of-Work-A-Complete-Mastering-Guide-Book-39a0535d4036806c92f8f5ba643d5d65
    //! 2) "The Repository Pattern & `IEnumerable` vs `IQueryable`, Expression Trees":
    //>     https://app.notion.com/p/The-Repository-Pattern-IEnumerable-vs-IQueryable-Expression-Trees-The-Mastery-Guide-BookC-39b0535d403680849d6ad3e2a9e693b1
*/
public interface IBaseRepository<TEntity, TIdKeyType>
    where TEntity : ABaseEntity<TIdKeyType>, IAggregateRoot
    where TIdKeyType : notnull
{
    #region Reading (Queries)

    //? Loads the WHOLE aggregate, TRACKED — the change tracker is what makes the mutation that follows persistable.
    //! Global query filters apply → a soft-deleted row is invisible here (that is the point).
    Task<TEntity?> GetByIdAsync(TIdKeyType id, CancellationToken ct = default);

    //! Avoid on large tables — this is the write model. For a paged/filtered LIST, use Dapper (read model).
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default);

    //? Existence check — compiles to SELECT CASE WHEN EXISTS(...); never materialises the entity.
    Task<bool> IsExistsAsync(TIdKeyType id, CancellationToken ct = default);

    #endregion Reading (Queries)


    #region Writing (Commands) — all of these only STAGE the change

    Task AddAsync(TEntity entity, CancellationToken ct = default);

    //? Only needed for a DETACHED entity. A tracked one is already watched by the snapshot comparer.
    void Update(TEntity entity);

    /*
        //! THE ONE "delete" the application should call. It is POLICY-AWARE, not a raw Remove():
        //>     - entity is [IDeletable]  → calls the DOMAIN method [MarkAsDeleted()] → the aggregate raises its own
        //>                                  event (e.g. CategoryDeletedDomainEvent) and can refuse (AlreadyDeleted → 409).
        //>     - otherwise                → physical [Set.Remove(entity)].
        //! Returns [Result] precisely BECAUSE the domain is allowed to refuse. A repository that swallowed that
        //! refusal would turn a 409 Conflict into a silent no-op.
    */
    Result Delete(TEntity entity);

    //? Convenience: load-then-Delete. Returns [EntityStateErrors.NotFound(...)] when the id resolves to nothing.
    Task<Result> DeleteByIdAsync(TIdKeyType id, CancellationToken ct = default);

    /*
        //! [HARD DELETE] — the physical DELETE escape hatch. Bypasses IDeletable entirely.
        //! Legitimate uses: GDPR erasure, purging an expired row, a pure join-table entry.
        //! Named [HardDelete] and NOT [Delete] on purpose: a physical delete must be a CHOICE you can grep for,
        //! never something you get by accident from calling the obvious method.
    */
    void HardDelete(TEntity entity);

    #endregion Writing (Commands)
}
