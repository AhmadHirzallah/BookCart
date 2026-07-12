using System.Linq.Expressions;
using BookCart.Application.Common.Abstractions.Data.Repositories;
using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Common.Abstractions.Errors;
using BookCart.Domain.Common.Results;
using BookCart.Infrastructure.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace BookCart.Infrastructure.Persistence.Repositories;

/*
    //! 1) Check my Notes of "The Repository Pattern & Aggregate Roots & Unit of Work": https://app.notion.com/p/The-Repository-Pattern-only-with-aggregate-roots-Unit-of-Work-A-Complete-Mastering-Guide-Book-39a0535d4036806c92f8f5ba643d5d65
    //! 2) "The Repository Pattern & `IEnumerable` vs `IQueryable`, Expression Trees":
    //>     https://app.notion.com/p/The-Repository-Pattern-IEnumerable-vs-IQueryable-Expression-Trees-The-Mastery-Guide-BookC-39b0535d403680849d6ad3e2a9e693b1
*/
internal abstract class ABaseRepository<TEntity, TIdKeyType> : IBaseRepository<TEntity, TIdKeyType>
    where TEntity : ABaseEntity<TIdKeyType>, IAggregateRoot
    where TIdKeyType : notnull
{
    #region Construction & Fields

    protected BookCartDbContext DbContext { get; }

    //? The write-side set. [DbContext.Set<T>()] is internally cached by EF, but holding it is cheaper to read.
    protected DbSet<TEntity> Set { get; }

    protected ABaseRepository(BookCartDbContext dbContext)
    {
        DbContext = dbContext;
        Set = dbContext.Set<TEntity>();
    }

    #endregion Construction & Fields


    #region Query Composition — [protected]: Infrastructure-only building blocks


    protected virtual IQueryable<TEntity> IncludeAggregate(IQueryable<TEntity> query) => query;

    protected IQueryable<TEntity> Query(
        bool tracked = true,
        bool includeAggregate = true,
        bool ignoreQueryFilters = false
    )
    {
        IQueryable<TEntity> query = Set;

        if (ignoreQueryFilters)
        {
            query = query.IgnoreQueryFilters();
        }

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        if (includeAggregate)
        {
            query = IncludeAggregate(query);
        }

        return query;
    }

    protected static Expression<Func<TEntity, bool>> IdEquals(TIdKeyType id) =>
        entity => entity.Id!.Equals(id);

    protected async Task<IReadOnlyList<TEntity>> FindUsingPredicateAsync(
        Expression<Func<TEntity, bool>> predicate,
        bool tracked = false,
        bool ignoreQueryFilters = false,
        CancellationToken ct = default
    ) =>
        await Query(tracked, includeAggregate: tracked, ignoreQueryFilters)
            .Where(predicate)
            .ToListAsync(ct);

    //? Single match. [tracked: true] by default — the usual caller is "load THE one row I am about to change".
    protected async Task<TEntity?> FindOneUsingPredicateAsync(
        Expression<Func<TEntity, bool>> predicate,
        bool tracked = true,
        bool ignoreQueryFilters = false,
        CancellationToken ct = default
    ) =>
        await Query(tracked, includeAggregate: tracked, ignoreQueryFilters)
            .FirstOrDefaultAsync(predicate, ct);

    protected async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        bool ignoreQueryFilters = false,
        CancellationToken ct = default
    ) =>
        await Query(tracked: false, includeAggregate: false, ignoreQueryFilters)
            .AnyAsync(predicate, ct);

    protected async Task<int> CountAsync(
        Expression<Func<TEntity, bool>> predicate,
        bool ignoreQueryFilters = false,
        CancellationToken ct = default
    ) =>
        await Query(tracked: false, includeAggregate: false, ignoreQueryFilters)
            .CountAsync(predicate, ct);

    protected async Task<TEntity?> FindByIdIgnoringFiltersAsync(
        TIdKeyType id,
        CancellationToken ct = default
    ) => await FindOneUsingPredicateAsync(IdEquals(id), tracked: true, true, ct);

    #endregion Query Composition


    #region Reading (Queries) — [public]: the IBaseRepository contract

    //! TRACKED + whole aggregate: this is the "load it so the domain can change it" path.
    public async Task<TEntity?> GetByIdAsync(TIdKeyType id, CancellationToken ct = default) =>
        await FindOneUsingPredicateAsync(IdEquals(id), tracked: true, false, ct);

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default) =>
        await Query(tracked: false).ToListAsync(ct);

    public async Task<bool> IsExistsAsync(TIdKeyType id, CancellationToken ct = default) =>
        await AnyAsync(IdEquals(id), false, ct);

    #endregion Reading (Queries)


    #region Writing (Commands) — STAGE ONLY; IUnitOfWork.SaveChangesAsync() commits


    public async Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public void Update(TEntity entity)
    {
        if (DbContext.Entry(entity).State == EntityState.Detached)
        {
            Set.Update(entity);
        }
    }

    /*
        //!  POLICY-AWARE DELETE — one meaning of "delete" for the whole application.
        //>     - [IDeletable] (every AMasterEntity, so Category)  → SOFT: call the entity's OWN [MarkAsDeleted()].
        //>       That is a DOMAIN method: it enforces the invariant (refuses if already deleted → 409 Conflict)
        //>       and raises the aggregate's event (CategoryDeletedDomainEvent). NEVER set IsDeleted = true from
        //>       out here — that would bypass both, and the setter is private precisely to stop you.
        //>     - Not IDeletable (a pure join row, say)            → HARD: physical Set.Remove.
        //
        //!  REQUIRES A TRACKED ENTITY. A soft delete IS an UPDATE, and EF only writes UPDATEs for entities it is
        //!  tracking — which is why GetByIdAsync above loads tracked. Hand this a no-tracking entity and it will
        //!  flip the flag in memory and persist absolutely nothing.
        //
        //!  Returns [Result] because the domain is ALLOWED TO REFUSE. Swallowing that would turn a 409 into a
        //!  silent success. Note [Result] carries an implicit operator from Error, hence the bare returns.
    */
    public Result Delete(TEntity entity)
    {
        if (entity is IDeletable deletable)
        {
            return deletable.MarkAsDeleted();
        }

        Set.Remove(entity);
        return Result.Success();
    }

    /*
        //? Load-then-delete. Uses [GetByIdAsync] → global filters apply → an ALREADY soft-deleted row is invisible
        //? and resolves to NotFound (not "AlreadyDeleted"). That is intentional: to the application, a soft-deleted
        //? Category does not exist. Use [FindByIdIgnoringFiltersAsync] if you truly need to reach a hidden row.
        //!  [typeof(TEntity).Name] is what lets a GENERIC base emit an aggregate-specific code — "Category.NotFound".
    */
    public async Task<Result> DeleteByIdAsync(TIdKeyType id, CancellationToken ct = default)
    {
        TEntity? entity = await GetByIdAsync(id, ct);

        if (entity is null)
        {
            //! Implicit: Error → Result (failure)
            return EntityStateErrors.NotFound(typeof(TEntity).Name);
        }

        return Delete(entity);
    }

    /*
        //!  [HARD DELETE] — physical DELETE, bypassing IDeletable. GDPR erasure, purges, join rows.
        //!  Deliberately NOT named [Delete]: destroying a row must be something you TYPED ON PURPOSE and can grep
        //!  for in review — never something you receive by calling the obvious method.
        //
        //?  Why not [ExecuteDeleteAsync()]? It issues DELETE straight to the database, bypassing the change
        //?  tracker entirely → it ignores SaveChangesAsync (breaking the Unit of Work's atomicity) and NO domain
        //?  event ever fires. It is a BULK tool ("purge every cart abandoned > 90 days") and belongs in a
        //?  purpose-named method on a concrete repository, never in the generic per-entity path.
    */
    public void HardDelete(TEntity entity) => Set.Remove(entity);

    #endregion Writing (Commands)
}
