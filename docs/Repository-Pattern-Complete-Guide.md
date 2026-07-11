# The Repository Pattern & Unit of Work — A Complete Mastering Guide (BookCart)

> **Goal:** understand *why* the repository exists, where it sits in Clean Architecture, the exact
> rules (repository-per-aggregate, Unit of Work owns the commit, don't leak `IQueryable`), how to
> design every method, and the real end-to-end flow of a command. Includes your interface fixed,
> a full EF implementation, and graded exercises with solutions.

---

## Part 0 — The one-paragraph mental model

> **A repository is an *in-memory collection of aggregate roots* that happens to be backed by a
> database.** You `Add` to it, you `Get` from it, you `Remove` from it — as if it were a
> `List<Category>` living in memory. The fact that behind the scenes it's SQL Server, Postgres, or a
> file is a **secret the repository keeps**, so your Application and Domain never mention a database,
> a connection, or a query. Swap the database and your business logic doesn't change one line.

That "collection illusion" is the whole point. Everything below serves it.

---

## Part 1 — Why it exists (the problems it solves)

Without a repository, your command handlers are full of `DbContext`, LINQ, and SQL. Four costs:

1. **Coupling.** Business logic depends on EF Core / SQL Server. Changing the DB ripples everywhere.
2. **Untestable.** To unit-test "create a category," you'd need a real database.
3. **Duplication.** The same "get active category by name" query is re-written in five handlers.
4. **Leaky domain.** Persistence concerns (tracking, transactions, includes) pollute domain code.

The repository is the **seam** that fixes all four: one place that speaks "aggregates in, aggregates
out," mockable in tests, DB-agnostic to callers.

```
        Application (handlers)  ──depends on──▶  IRepository  (interface)   ← the abstraction
                                                     ▲
                                                     │ implements
        Infrastructure         ──────────────  EfRepository  (EF Core)      ← the detail
```

**Dependency Inversion in one picture:** the interface lives where it's *used* (Application), the
implementation lives in Infrastructure. High-level policy doesn't depend on low-level detail; both
depend on the abstraction. This is why `IBaseRepository` is correctly in
`BookCart.Application.Common.Abstractions.Data`.

---

## Part 2 — The five non-negotiable rules

### Rule 1 — One repository **per aggregate root**, never per table

A repository is for **aggregate roots** (`Category`, `Book`, `Order`) — the entities that are the
entry point to a consistency boundary. You do **not** create a repository for a *child* entity
(`OrderLine`, a value object). You reach children **through** their root: `order.AddLine(...)`, then
save the `Order`. This is why the earlier capstone suggested constraining to `IAggregateRoot`:

```csharp
where TEntity : ABaseEntity<TId>, IAggregateRoot
```

> If you find yourself wanting `IOrderLineRepository`, stop — load the `Order` and mutate it. The
> aggregate root guards the invariants of its children; a child repository would bypass them.

### Rule 2 — The repository **stages**; the **Unit of Work commits**

A repository does **not** call `SaveChanges`. It records intentions ("add this," "remove that"). A
separate **Unit of Work** wraps all those intentions in **one transaction** and commits them
together — so a command that touches three aggregates either fully succeeds or fully rolls back.

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

**With EF Core, the `DbContext` *is* both** the repository backing store and the Unit of Work
(`context.SaveChangesAsync()` is the commit). Keeping them as *separate interfaces* still matters:
handlers decide **when** to commit, independently of any single repository.

> ❌ If every `AddAsync`/`DeleteAsync` calls `SaveChanges` internally, you lose transactionality:
> two writes in one command become two transactions, and a failure between them corrupts state.

### Rule 3 — Return **domain-friendly** shapes; never leak `IQueryable`

- Single lookup → **`TEntity?`** (nullable literally means "may not exist").
- Many → **`IReadOnlyList<TEntity>`** (materialized, immutable to the caller).
- **Never return `IQueryable<TEntity>`.** That hands the caller a half-built SQL query and re-opens
  the leak you closed — now the Application layer is composing database queries again.

### Rule 4 — The repository returns **data**, not `Result`

Repositories are thin data access; they return the entity or `null`. **Translating "not found" into
a `Result`/error is the *handler's* job** (it knows the business meaning). Keep repos free of your
`Result`/`Error` types — it keeps them dumb and reusable.

### Rule 5 — Generic base for CRUD, **specific** repos for queries

A generic `IRepository<TEntity, TId>` covers the universal 20% (get/add/remove). Entity-specific
queries ("category by name," "books cheaper than X") go on a **specific** interface that extends it.
Don't cram every query into the generic base — that's how repositories rot into 40-method god-types.

```csharp
public interface ICategoryRepository : IRepository<Category, CategoryId>
{
    Task<Category?> GetByNameAsync(CategoryName name, CancellationToken ct = default);
}
```

---

## Part 3 — Your interface, fixed and explained

```csharp
using BookCart.Domain.Common.Abstractions;

namespace BookCart.Application.Common.Abstractions.Data;

//! Encapsulates data access so Application/Domain stay ignorant of SQL Server / EF / storage.
//! Generic base = the universal CRUD. Entity-specific queries live on derived interfaces.
public interface IRepository<TEntity, TId>
    where TEntity : ABaseEntity<TId>, IAggregateRoot   // ← repositories are for AGGREGATE ROOTS
    where TId : notnull
{
    #region Reads (queries) — return data or null, never IQueryable

    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default);              // null = not found
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default);          // ← was the bug: a collection
    Task<bool> ExistsAsync(TId id, CancellationToken ct = default);                    // cheap existence check

    #endregion

    #region Writes (commands) — STAGE changes; IUnitOfWork.SaveChangesAsync COMMITS them

    Task AddAsync(TEntity entity, CancellationToken ct = default);
    void Update(TEntity entity);   // sync: just marks modified (see §5 — often unnecessary with tracking)
    void Remove(TEntity entity);   // hard delete; for SOFT delete see §6

    #endregion
}
```

**What changed vs your version and why:**
- `GetAllAsync` → `Task<IReadOnlyList<TEntity>>` (a list, not a single nullable).
- Removed the redundant `TId id` from `Update`/`Delete` — the entity already carries `Id`.
- `Update`/`Remove` are **`void`** (they just stage; see §5). Only `AddAsync` is async because EF's
  `AddAsync` can hit a value generator; `Add`/`Remove`/`Update` are synchronous in EF.
- Consistent `ct = default` everywhere.
- Added `ExistsAsync` (very common — validating "does this category exist?" without loading it).
- Added the `IAggregateRoot` constraint (Rule 1).
- **Commit removed from the repo** — it lives in `IUnitOfWork` (Rule 2).

---

## Part 4 — The DELETE question you asked (by-id vs by-entity, hard vs soft)

This is the crux of your `DeleteByIdAsync` vs `DeleteEntityAsync` question. Four combinations:

| Style | Signature | When it's right | Cost |
|-------|-----------|-----------------|------|
| **Hard, by entity** | `void Remove(TEntity e)` | you already loaded the aggregate | needs the entity in memory |
| **Hard, by id** | `Task RemoveByIdAsync(TId id, ct)` | you only have the id; want to skip loading | can't raise domain events (no entity) |
| **Soft, by entity** | *not a repo method* → `entity.MarkAsDeleted(); Update(e);` | your `AMasterEntity` has `IsDeleted` | must load the entity |
| **Soft, by id** | load → `MarkAsDeleted()` → save | id-only caller, but you still load | one extra read |

**For BookCart specifically:** your `AMasterEntity` has `IsDeleted` → you want **soft delete**. And
soft delete is really an **update** (flip a flag), ideally through a **domain method** so the
aggregate can raise a `CategoryDeletedDomainEvent`:

```csharp
// on the entity (rich domain model):
public Result MarkAsDeleted()
{
    if (IsDeleted) return CategoryErrors.AlreadyDeleted;
    IsDeleted = true;
    RaiseDomainEvent(new CategoryDeletedDomainEvent(Id!));
    return Result.Success();
}
```

So the **handler** does: `GetByIdAsync` → `entity.MarkAsDeleted()` → `SaveChangesAsync`. The repo
doesn't need a `Delete` at all for the soft-delete path — it needs `Update` (or nothing, if the
entity is tracked). Keep `Remove(entity)` only for the rare **hard delete** (GDPR "erase," cleanup).

**Recommendation:** don't expose both `DeleteById` and `DeleteEntity` by reflex. Expose
`Remove(entity)` for hard delete, and drive soft delete through the aggregate's own
`MarkAsDeleted()`. Add `RemoveByIdAsync` **only** if you have a real "delete without loading and
without events" use case.

> **Bonus — make soft-deleted rows invisible automatically** with an EF **global query filter**, so
> every query excludes them without you writing `.Where(x => !x.IsDeleted)` each time:
> ```csharp
> modelBuilder.Entity<Category>().HasQueryFilter(c => !c.IsDeleted);
> ```

---

## Part 5 — The `Update` subtlety (why it's often unnecessary)

With EF Core **change tracking**, an entity you loaded via `GetByIdAsync` is *tracked*. If you mutate
it and call `SaveChangesAsync`, EF **detects the change and writes it** — **no `Update` call needed**:

```csharp
var category = await repo.GetByIdAsync(id, ct);   // tracked
category!.Rename("New Name");                       // mutate
await unitOfWork.SaveChangesAsync(ct);              // EF writes the UPDATE automatically
```

`Update(entity)` is for the **disconnected** scenario: an entity that EF is *not* tracking (e.g.
rebuilt from an API DTO), where you must explicitly tell EF "this exists, mark it modified." Know the
difference — calling `Update` on an already-tracked entity is redundant and can over-write columns.

---

## Part 6 — The full concrete implementation (Infrastructure)

```csharp
using BookCart.Application.Common.Abstractions.Data;
using BookCart.Domain.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BookCart.Infrastructure.Persistence.Repositories;

//! Lives in Infrastructure — the ONLY project that knows EF Core exists.
public class EfRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : ABaseEntity<TId>, IAggregateRoot
    where TId : notnull
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<TEntity> Set;

    public EfRepository(AppDbContext context)
    {
        Context = context;
        Set = context.Set<TEntity>();
    }

    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.Id!.Equals(id), ct);

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default) =>
        await Set.ToListAsync(ct);

    public Task<bool> ExistsAsync(TId id, CancellationToken ct = default) =>
        Set.AnyAsync(e => e.Id!.Equals(id), ct);

    public async Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public void Update(TEntity entity) => Set.Update(entity);   // stages; no SaveChanges here

    public void Remove(TEntity entity) => Set.Remove(entity);   // stages a hard delete
}
```

```csharp
// AppDbContext IS the Unit of Work:
public class AppDbContext : DbContext, IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        base.SaveChangesAsync(ct);   // the ONE commit for the whole transaction
}
```

```csharp
// Specific repository adds entity-specific queries:
public class CategoryRepository : EfRepository<Category, CategoryId>, ICategoryRepository
{
    public CategoryRepository(AppDbContext context) : base(context) { }

    public Task<Category?> GetByNameAsync(CategoryName name, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(c => c.Name == name, ct);
}
```

Registration (DI, in Infrastructure):
```csharp
services.AddScoped<ICategoryRepository, CategoryRepository>();
services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
```

---

## Part 7 — The REAL end-to-end flow (this is the "master it" moment)

A `CreateCategory` command, from HTTP to database, showing exactly who does what:

```csharp
public sealed record CreateCategoryCommand(string Name, string Description);

public class CreateCategoryHandler
{
    private readonly ICategoryRepository _categories;   // the abstraction
    private readonly IUnitOfWork _unitOfWork;           // the committer

    public CreateCategoryHandler(ICategoryRepository categories, IUnitOfWork unitOfWork)
    {
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CategoryId>> Handle(CreateCategoryCommand cmd, CancellationToken ct)
    {
        // 1. Business rule the DOMAIN can't check alone (uniqueness spans the whole table):
        if (await _categories.GetByNameAsync(/* CategoryName.Create(...).Value */ default!, ct) is not null)
            return CategoryErrors.NameAlreadyExists;          // handler maps to a Result

        // 2. Build the aggregate through its FACTORY (validates + raises CategoryCreatedDomainEvent):
        Result<Category> categoryResult = Category.Create(cmd.Name, cmd.Description);
        if (categoryResult.IsFailure)
            return categoryResult.Errors;                     // Error[] → Result<CategoryId>

        Category category = categoryResult.Value;

        // 3. STAGE the insert (no DB write yet):
        await _categories.AddAsync(category, ct);

        // 4. COMMIT — the Unit of Work writes everything in ONE transaction, and (typically) an
        //    interceptor here dispatches the queued domain events after a successful save:
        await _unitOfWork.SaveChangesAsync(ct);

        return category.Id!;                                   // CategoryId → Result<CategoryId>
    }
}
```

**Who owns which responsibility — memorize this division of labor:**

| Concern | Owner |
|---------|-------|
| Field/format validation (name length) | the **Value Object** (`CategoryName.Create`) |
| Aggregate invariants + raising events | the **Entity factory** (`Category.Create`) |
| Cross-aggregate rules (uniqueness) | the **Handler** (queries via repo) |
| Fetching/staging aggregates | the **Repository** |
| Transaction / commit / event dispatch | the **Unit of Work** |
| SQL, tracking, includes | the **EF implementation** (Infrastructure) |

---

## Part 8 — The mature debate: "repository over EF is redundant"

You'll hear seniors argue **against** repositories ("EF's `DbContext` + `DbSet` *already* is a
repository + unit of work — wrapping it is ceremony"). Know both sides:

- **Pro-repository:** DB-agnostic Application, trivially mockable handlers, one home for queries,
  enforce aggregate-root access, hide EF entirely. Best when you value testability and layering.
- **Anti-repository (use `DbContext` directly):** less code, full LINQ power, no leaky abstraction
  pretending EF isn't there. Best in small apps or when the team is disciplined.

**The pragmatic middle (what most Clean Architecture BookCart-style projects do):**
- A **generic repository** for the boring CRUD + **specific repositories** for real queries.
- **Never expose `IQueryable`** (that's the abstraction leak both camps hate).
- Add the **Specification pattern** when query combinations explode (encapsulate a `where`+`include`
  +`orderby` as a first-class object) — but don't reach for it before you feel the pain.

---

## Part 9 — EXERCISES (design + reasoning; solutions in §10)

**R1.** Spot every problem in this interface and rewrite it:
```csharp
public interface IProductRepo
{
    Product GetById(Guid id);
    IQueryable<Product> GetAll();
    void Save(Product p);   // calls SaveChanges internally
}
```

**R2.** A command must (a) create an `Order`, (b) decrement `Book.Stock`, (c) both-or-neither. Which
component guarantees "both-or-neither," and what single call makes it happen?

**R3.** You need "get all books in category X, cheapest first, page 2 (20 per page)." Should this go
on the generic `IRepository<Book, BookId>` or `IBookRepository`? Write the signature.

**R4.** For BookCart soft delete, write the handler steps for `DeleteCategory(CategoryId id)` — name
the exact calls and who raises the domain event.

**R5.** Why is `GetByIdAsync` returning `Task<TEntity?>` (nullable) *better* than throwing
`NotFoundException` inside the repository? Give two reasons.

**R6.** Your teammate adds `Task<int> SaveChangesAsync()` to `IRepository`. Explain in one sentence
why that breaks transactionality, and what to do instead.

---

## Part 10 — SOLUTIONS

**R1.** Problems: sync (should be async + `CancellationToken`); non-nullable return (can't express
"not found"); **leaks `IQueryable`**; `Save` calls `SaveChanges` (kills Unit of Work); uses `Guid`
instead of a strongly-typed id; no aggregate-root constraint.
```csharp
public interface IProductRepository : IRepository<Product, ProductId>
{
    // reads/writes inherited; add only Product-specific queries here
}
```

**R2.** The **Unit of Work** guarantees atomicity; **`await _unitOfWork.SaveChangesAsync(ct)`** — a
single commit — makes (a) and (b) succeed or roll back together. The repositories only *stage* the
insert and the stock change.

**R3.** Specific repo (`IBookRepository`) — it's a Book-specific query with paging:
```csharp
Task<IReadOnlyList<Book>> GetByCategoryAsync(
    CategoryId categoryId, int page, int pageSize, CancellationToken ct = default);
```
(Return a materialized list; do the `OrderBy`/`Skip`/`Take` **inside** the implementation, never by
handing back `IQueryable`.)

**R4.**
```
1. var category = await _categories.GetByIdAsync(id, ct);   // load the aggregate (tracked)
2. if (category is null) return CategoryErrors.NotFound;     // handler maps null → Result
3. Result r = category.MarkAsDeleted();                      // ENTITY sets IsDeleted + raises CategoryDeletedDomainEvent
4. if (r.IsFailure) return r.Errors;
5. await _unitOfWork.SaveChangesAsync(ct);                   // commit; interceptor dispatches the event
```
The **entity** (`MarkAsDeleted`) raises the event — not the repo, not the handler.

**R5.** (1) **Not-found is a normal, expected outcome** of a lookup, not an exceptional one —
exceptions for control flow are slow and noisy. (2) The **repository shouldn't decide business
meaning**; only the handler knows whether "missing" is a 404, a no-op, or an error — returning
`null` lets each caller choose. (Bonus: nullable annotations make the compiler force the caller to
handle it.)

**R6.** It lets a *single repository* commit independently, so a command spanning two repositories
produces two transactions — a mid-command failure leaves half-written state. Put `SaveChangesAsync`
on a separate **`IUnitOfWork`** that the handler commits once.

---

## Part 11 — Cheat sheet

```
WHAT
  Repository = in-memory-collection illusion over aggregate roots; hides the database entirely.

LAYERS (Clean Architecture)
  interface  → Application (Common/Abstractions/Data)   ← callers depend on this
  impl       → Infrastructure (EF Core)                 ← the only project that knows SQL

FIVE RULES
  1. one repo per AGGREGATE ROOT (constrain : IAggregateRoot); never per child/table
  2. repo STAGES; IUnitOfWork.SaveChangesAsync COMMITS (one transaction per command)
  3. return TEntity? (single) / IReadOnlyList<T> (many); NEVER IQueryable
  4. repo returns DATA (entity/null); the HANDLER maps null → Result/Error
  5. generic base for CRUD + specific interfaces for real queries

METHOD SHAPES
  Task<TEntity?>            GetByIdAsync(TId id, ct)
  Task<IReadOnlyList<T>>   GetAllAsync(ct)
  Task<bool>               ExistsAsync(TId id, ct)
  Task                     AddAsync(TEntity e, ct)       // async: value generators
  void                     Update(TEntity e)             // often unneeded (change tracking)
  void                     Remove(TEntity e)             // HARD delete
  (soft delete)            entity.MarkAsDeleted() + SaveChanges   ← via domain method + query filter

DELETE DECISION
  soft (IsDeleted)  → load → entity.MarkAsDeleted() (raises event) → commit   ← BookCart default
  hard              → Remove(entity)   |   by-id only + no events → RemoveByIdAsync (add only if needed)

WHO DOES WHAT
  VO.Create → field rules | Entity.Create → invariants+events | Handler → cross-aggregate rules
  Repository → fetch/stage | UnitOfWork → commit/dispatch | EF impl → SQL/tracking
```

### TL;DR
Your instinct is right — but fix `GetAll` (return a list), drop the duplicate `id` params, and add a
**Unit of Work** so the repo *stages* and the UoW *commits*. Repository-per-aggregate-root, return
`TEntity?`/`IReadOnlyList<T>` (never `IQueryable`), let the **handler** turn `null` into a `Result`,
and drive **soft delete** through the entity's own `MarkAsDeleted()` (+ an EF global query filter).
Generic base for CRUD, specific interfaces for queries.
```
