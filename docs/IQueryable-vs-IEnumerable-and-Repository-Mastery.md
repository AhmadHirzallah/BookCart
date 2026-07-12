# `IEnumerable` vs `IQueryable`, Expression Trees & The Repository — The Mastery Guide (BookCart)

> **Goal:** destroy every wrong intuition about `IEnumerable` / `IQueryable`, understand *exactly*
> what an expression tree is and why it is the entire difference, learn the five bugs that this
> confusion causes in production, and then see why every design decision in `ABaseRepository`
> (abstract class + interface, `protected` query surface, no leaked `IQueryable`) follows from it.
> Ends with three real aggregates, real DI, real handlers, 12 graded exercises with solutions, and a
> 14-day plan.
>
> **Companion to:** [`Repository-Pattern-Complete-Guide.md`](./Repository-Pattern-Complete-Guide.md)
> — that one covers the *rules and the flow*. This one covers the *machinery underneath them*.

---

## Part 0 — First, unlearn. Your current mental model is the common one, and it is wrong.

You wrote:

> *"`IQueryable` is making things late so I do not get data until I put all the conditions, and
> `IEnumerable` gets the data immediately."*

Half of that sentence is a real insight. The other half is a myth that will cost you a production
incident. Let's kill the myth first, because everything else depends on it.

### Myth: "`IEnumerable` executes immediately."

**It does not.** `IEnumerable<T>` is *also* lazy. Watch — this is plain C#, no database anywhere:

```csharp
var numbers = new List<int> { 1, 2, 3 };

IEnumerable<int> query = numbers.Where(n =>
{
    Console.WriteLine($"  filtering {n}");   //! side effect so we can SEE when it runs
    return n > 1;
});

Console.WriteLine("query built — nothing printed yet, right?");

foreach (int n in query)                     //! ← ONLY NOW does 'filtering 1/2/3' print
{
    Console.WriteLine($"got {n}");
}
```

Output:

```
query built — nothing printed yet, right?
  filtering 1
  filtering 2
got 2
  filtering 3
got 3
```

The filter did not run at `.Where(...)`. It ran at `foreach`. **`IEnumerable` is deferred too.**
LINQ-to-Objects operators are iterator blocks (`yield return`) — they build a lazy pipeline and do
nothing until someone pulls on it.

> ### 🧠 The single sentence to memorise
>
> **Deferred execution is NOT the difference. BOTH are lazy.**
> The difference is **WHERE your lambda runs**: `IEnumerable` runs it **in your C# process, on
> objects already in RAM**. `IQueryable` runs it **on the database server, as SQL**.

### So what *was* right in your sentence?

That `IQueryable` "waits until you put all the conditions" — yes. It accumulates `.Where().OrderBy().Skip()`
into **one** query and sends **one** SQL statement at the end. That part of your intuition is sound.
You just attached it to the wrong cause.

### The re-framing that makes everything click

| | `IEnumerable<T>` | `IQueryable<T>` |
|---|---|---|
| Mental model | **"a pipeline over objects I already have"** | **"a SQL query I am still writing"** |
| Your lambda is compiled to | a **delegate** (executable machine code) | an **expression tree** (inspectable *data*) |
| Filtering happens | in **RAM**, in your process | in the **database**, as a `WHERE` clause |
| Rows crossing the network | **all of them**, then you throw most away | **only the matching ones** |
| Both deferred? | ✅ yes | ✅ yes |

Everything from here on is an elaboration of that one row: **delegate vs expression tree.**

---

## Part 1 — The type hierarchy (and the trap hiding in it)

```csharp
public interface IEnumerable<out T> : IEnumerable
{
    IEnumerator<T> GetEnumerator();          //! that is LITERALLY all it is: "I can be looped over"
}

public interface IQueryable<out T> : IEnumerable<T>, IQueryable   //! ⚠️ IT *IS* AN IEnumerable
{
    Type           ElementType { get; }      //? what T am I?
    Expression     Expression  { get; }      //? THE QUERY-SO-FAR, as a data structure  ← the magic
    IQueryProvider Provider    { get; }      //? the thing that knows how to turn ↑ into SQL
}
```

Read that inheritance line again:

```csharp
IQueryable<T> : IEnumerable<T>
```

**`IQueryable` IS-A `IEnumerable`.** This is the trap that generates 90% of the bugs. It means:

- Every `IQueryable` can be silently used where an `IEnumerable` is expected — **no cast, no warning,
  no compiler error.** It just quietly stops being a database query.
- The instant that happens, you fall back to the RAM pipeline and **the whole table gets loaded**.

This is not hypothetical. It is Part 6, Bug #1, and it is the single most expensive mistake in this
entire topic.

The three extra members are the whole story. `IEnumerable` says *"I can be iterated."* `IQueryable`
says *"I can be iterated, **and** I am carrying a description of myself (`Expression`) plus an engine
that can translate that description into another language (`Provider`)."*

---

## Part 2 — The real difference: `Func<T,bool>` vs `Expression<Func<T,bool>>`

This is the heart of the topic. If you understand this part, you understand everything.

Look at the two `Where` methods. They are **different methods, on different static classes**:

```csharp
//! System.Linq.Enumerable  — LINQ to Objects
public static IEnumerable<T> Where<T>(
    this IEnumerable<T> source,
    Func<T, bool> predicate);                    //? a DELEGATE — compiled code, a pointer to a method

//! System.Linq.Queryable  — LINQ to Providers (EF Core, LINQ to SQL, Cosmos, ...)
public static IQueryable<T> Where<T>(
    this IQueryable<T> source,
    Expression<Func<T, bool>> predicate);        //? an EXPRESSION TREE — DATA describing the code
```

Now the payoff. You write the **exact same lambda** in both cases:

```csharp
book => book.Price > 100
```

but the compiler emits **two completely different things** depending on the target type.

### Case A — target is `Func<T,bool>` → a delegate

The compiler compiles the lambda body to IL, exactly like a tiny method:

```csharp
Func<Book, bool> predicate = book => book.Price > 100;

bool result = predicate(someBook);   //! you can CALL it. That is all you can do.
```

It is a **black box**. You can invoke it. You cannot look inside it. Nobody — not EF, not anyone —
can ask it *"what do you compare, and to what?"* It is machine code. **EF cannot translate machine
code into SQL.**

### Case B — target is `Expression<Func<T,bool>>` → a tree of objects

The compiler does something extraordinary: instead of compiling the lambda, it **emits code that
builds an object graph describing the lambda**.

```csharp
Expression<Func<Book, bool>> predicate = book => book.Price > 100;

//! You CANNOT call this directly. It is not code — it is a DATA STRUCTURE.
//! But you CAN take it apart and read it:

var body = (BinaryExpression)predicate.Body;

Console.WriteLine(predicate.NodeType);   // Lambda
Console.WriteLine(body.NodeType);        // GreaterThan          ← the OPERATOR
Console.WriteLine(body.Left);            // book.Price           ← a MemberExpression
Console.WriteLine(body.Right);           // 100                  ← a ConstantExpression
```

The tree literally looks like this in memory:

```
            LambdaExpression
                  │
            BinaryExpression  (NodeType = GreaterThan)
                 ╱                    ╲
     MemberExpression            ConstantExpression
      (Book.Price)                    (100)
            │
     ParameterExpression
          (book)
```

**Now put yourself in EF Core's shoes.** It is handed that tree. It walks it:

- Root is `GreaterThan` → *"SQL operator is `>`"*
- Left is a `MemberExpression` on `Book.Price` → *"look up my model… that property maps to column
  `[Price]` on table `[Books]`"*
- Right is a `ConstantExpression` of `100` → *"emit a parameter, `@__p_0 = 100`"*

and it prints:

```sql
SELECT [b].[Id], [b].[Title], [b].[Price]
FROM [Books] AS [b]
WHERE [b].[Price] > @__p_0
```

**That is the entire trick.** An expression tree is *source code represented as data*, so a library
can **read your C# and re-write it in another language**. That is why it is called a **provider** —
`Provider` is the translator, `Expression` is the thing being translated.

> ### 🧠 The sentence that unlocks the whole topic
>
> **`Func` is code you can RUN. `Expression` is code you can READ.**
> SQL translation requires *reading*. That is why — and the ONLY reason why — `IQueryable` needs
> `Expression<Func<>>` and `IEnumerable` is content with `Func<>`.

### Proof that they are convertible one way only

```csharp
Expression<Func<Book, bool>> expr = b => b.Price > 100;
Func<Book, bool> compiled = expr.Compile();   //! ✅ tree → code. LEGAL. (This is a real JIT compile.)

Func<Book, bool> fn = b => b.Price > 100;
Expression<Func<Book, bool>> back = fn;       //! ❌ code → tree. IMPOSSIBLE. Does not compile.
```

You can always burn a tree down into code. You can **never** reconstruct the tree from the code.
The information is gone. **This asymmetry is why the direction of your data flow matters so much,
and why `.AsEnumerable()` is a one-way door.**

### This is why `IdEquals` in your `ABaseRepository` is typed the way it is

Open `BookCart.Infrastructure/Persistence/Repositories/ABaseRepository.cs`:

```csharp
protected static Expression<Func<TEntity, bool>> IdEquals(TIdKeyType id) =>
    entity => entity.Id!.Equals(id);
```

Had that returned `Func<TEntity, bool>`, EF would have been handed a black box, `.Where()` would
have bound to `Enumerable.Where`, and **`GetByIdAsync` would have loaded your entire table into RAM
to find one row.** One word — `Expression` — is the difference between `WHERE [Id] = @p0` and a full
table scan dragged across the network. Same for every `protected` helper in that file.

---

## Part 3 — Deferred execution, precisely

Both are deferred. But *what* is deferred differs, and **what triggers it** is a fixed list worth
memorising.

### Execution triggers (this list is the whole thing)

Nothing happens until you hit one of these. Then, and only then, does SQL leave your machine.

| Category | Members | What it does |
|---|---|---|
| **Materialisers** | `ToList()` `ToListAsync()` `ToArray()` `ToDictionary()` `ToHashSet()` | Runs it, buffers **everything** into memory |
| **Aggregates** | `Count()` `Sum()` `Min()` `Max()` `Average()` | Runs it, returns **one scalar** (`SELECT COUNT(*)`) |
| **Element pickers** | `First()` `FirstOrDefault()` `Single()` `Last()` | Runs it, returns **one row** (`SELECT TOP(1)`) |
| **Existence** | `Any()` `All()` `Contains()` | Runs it, returns **one bool** (`SELECT EXISTS`) |
| **Iteration** | `foreach` / `await foreach` | Runs it, **streams** row by row |

**Everything else is lazy and just appends to the tree:** `Where` `Select` `OrderBy` `ThenBy`
`Skip` `Take` `GroupBy` `Join` `Include` `Distinct` `AsNoTracking` `IgnoreQueryFilters`.

Rule of thumb: **if it returns `IQueryable<T>` / `IEnumerable<T>`, it is lazy. If it returns anything
else (`List`, `int`, `bool`, `T`), it executed.**

### Watch a query get *composed* without executing

```csharp
IQueryable<Book> query = _dbContext.Books;                       //! no SQL
query = query.Where(b => b.Price > 100);                         //! no SQL — tree grows
query = query.Where(b => b.CategoryId == categoryId);            //! no SQL — tree grows
query = query.OrderBy(b => b.Title);                             //! no SQL — tree grows
query = query.Take(10);                                          //! no SQL — tree grows

//! Still ZERO database round-trips. You are HOLDING A QUERY, not data.
//! And you can PRINT it before running it — do this constantly, it is the master's habit:
Console.WriteLine(query.ToQueryString());

List<Book> books = await query.ToListAsync(ct);                  //! 💥 NOW. ONE round-trip. ONE SQL.
```

The two `.Where()` calls did **not** produce two queries. They were **merged** into one `WHERE ... AND ...`.
*That* is the composability your intuition was reaching for.

```sql
SELECT TOP(10) [b].[Id], [b].[Title], [b].[Price]
FROM [Books] AS [b]
WHERE [b].[Price] > @__p_0 AND [b].[CategoryId] = @__categoryId_1
ORDER BY [b].[Title]
```

### The deferred-execution gotcha nobody warns you about

Because the query is re-run **every time** you enumerate it:

```csharp
IQueryable<Book> expensive = _dbContext.Books.Where(b => b.Price > 100);

int count = expensive.Count();                    //! 💥 round-trip #1  (SELECT COUNT(*))
List<Book> list = expensive.ToList();             //! 💥 round-trip #2  (SELECT *)
bool any = expensive.Any();                       //! 💥 round-trip #3  (SELECT EXISTS)
```

**Three database hits.** Materialise once, then work in memory:

```csharp
List<Book> books = await _dbContext.Books.Where(b => b.Price > 100).ToListAsync(ct);  //! ONE hit

int count = books.Count;      //! ✅ free — it's a List, this is just a field
bool any  = books.Count > 0;  //! ✅ free
```

This applies to lazy `IEnumerable` too (re-enumerating re-runs the pipeline). The analyser rule is
called **"possible multiple enumeration"** — when your IDE flags it, it is telling you exactly this.

---

## Part 4 — Where the code runs: LINQ to Objects vs LINQ to Provider

Same syntax. Two universes.

```csharp
//! ═══ UNIVERSE 1: LINQ to Objects ═══════════════════════════════════════
//! Source is in RAM. Enumerable.Where(). Func<>. Runs in YOUR process.
List<Book> booksInMemory = GetBooksSomehow();
var cheap = booksInMemory.Where(b => b.Price < 50);      //? loops the List, calls a delegate per item

//! ═══ UNIVERSE 2: LINQ to Provider (EF Core) ════════════════════════════
//! Source is a DbSet. Queryable.Where(). Expression<>. Runs on SQL SERVER.
var cheap2 = _dbContext.Books.Where(b => b.Price < 50); //? builds a tree → becomes WHERE [Price] < 50
```

**Superpower of Universe 1:** you can call *anything*. Any C# method, any regex, any of your own code.
It's just C#.

**Curse of Universe 1:** the database already sent you every row. The filtering is damage control.

**Superpower of Universe 2:** the database does the work. Indexes get used. 10 rows cross the wire
instead of 10 million.

**Curse of Universe 2:** you may only write things the provider **knows how to translate**. There is
no `MyCustomBusinessRule()` in SQL:

```csharp
//! ❌ EF Core throws at RUNTIME: "The LINQ expression could not be translated."
var bad = await _dbContext.Books
    .Where(b => MyCustomPriceRule(b))          //? EF walks the tree, sees a MethodCallExpression to
    .ToListAsync(ct);                          //? a method it has never heard of, and gives up.
```

EF Core 3.0+ **throws** here rather than silently doing it in memory — a deliberate, excellent
breaking change. Earlier versions would quietly download your whole table and evaluate client-side,
and people only found out when production fell over. **A translation exception is a gift.** Read it,
don't suppress it.

The fix is to express the rule in translatable terms, or to do the untranslatable part *after*
materialising:

```csharp
//! ✅ narrow in SQL first (cheap), then apply the untranslatable rule in RAM (on 10 rows, not 10M)
List<Book> candidates = await _dbContext.Books
    .Where(b => b.Price > 100)                 //? translatable → runs in SQL
    .ToListAsync(ct);                          //? 💥 executes; now we are in Universe 1

List<Book> final = candidates
    .Where(b => MyCustomPriceRule(b))          //? ✅ legal now — plain C# on a List
    .ToList();
```

**That ordering — narrow in SQL, refine in RAM — is the professional pattern.** The bug is doing it
backwards.

---

## Part 5 — The overload-resolution trap (why the bugs are *invisible*)

Here is what makes this topic genuinely dangerous: **the compiler picks `Enumerable.Where` vs
`Queryable.Where` based on the STATIC TYPE of the variable — and it never warns you.**

```csharp
DbSet<Book> books = _dbContext.Books;

//! ── the variable's declared type decides EVERYTHING ──

IQueryable<Book> asQueryable = books;
var a = asQueryable.Where(b => b.Price > 100);   //! → Queryable.Where  → SQL WHERE      ✅

IEnumerable<Book> asEnumerable = books;          //! ⚠️ legal! IQueryable IS-A IEnumerable
var b2 = asEnumerable.Where(b => b.Price > 100); //! → Enumerable.Where → SELECT * then filter in RAM ❌
```

**Identical lambda. Identical-looking code. One does a `WHERE`. One downloads the table.** The only
difference is a type annotation four lines up. There is no squiggle, no warning, no error. It
compiles perfectly and passes every unit test against an in-memory list.

It falls over in production, on the table with 4 million rows.

> ### 🚨 The law
> **`var` is not your friend here.** When the source is a database, be explicit: write
> `IQueryable<T>`, and be *suspicious* every single time you see `IEnumerable<T>` near a `DbContext`.

This is the entire reason `ABaseRepository.Query()` is declared to return `IQueryable<TEntity>` and
not `var` / `IEnumerable<TEntity>`. Look at it again with new eyes:

```csharp
protected IQueryable<TEntity> Query(...)     //! ← IQueryable. Deliberate. Load-bearing.
{
    IQueryable<TEntity> query = Set;         //! ← IQueryable. Deliberate. Load-bearing.
    ...
    return query;
}
```

Change either of those two words to `IEnumerable` and every read in your entire application silently
becomes a full table scan, with no error anywhere. **That is how much weight one keyword carries.**

---

## Part 6 — The five bugs this confusion causes (all real, all common)

### Bug #1 — `AsEnumerable()` / `ToList()` too early *(the table-downloader)*

```csharp
//! ❌ CATASTROPHIC
List<Book> all = await _dbContext.Books.ToListAsync(ct);   //! 💥 SELECT * FROM Books — ALL 4,000,000
Book? found = all.FirstOrDefault(b => b.Isbn == isbn);     //!    then scan RAM for one

//! ✅ CORRECT
Book? found = await _dbContext.Books
    .FirstOrDefaultAsync(b => b.Isbn == isbn, ct);         //! SELECT TOP(1) ... WHERE [Isbn] = @p0
```

Both compile. Both return the right answer. One takes 4 milliseconds; the other takes 40 seconds and
OOMs the pod. **The moment you cross into `IEnumerable`, the database is done helping you.**

**Where this actually shows up in real code:** a helper that returns the wrong type.

```csharp
//! ❌ this innocent-looking method is a landmine
public IEnumerable<Book> GetBooks() => _dbContext.Books;   //! silently degrades to IEnumerable

//! every caller now filters in RAM, and none of them can tell:
var expensive = GetBooks().Where(b => b.Price > 100);      //! SELECT * FROM Books. Every time.
```

### Bug #2 — passing a `Func<>` variable to `IQueryable.Where`

```csharp
Func<Book, bool> predicate = b => b.Price > 100;           //! ⚠️ a DELEGATE, not an Expression

var result = _dbContext.Books.Where(predicate);            //! ← what happened here?
```

`Queryable.Where` demands an `Expression<Func<>>`. A `Func<>` doesn't match. So overload resolution
**falls back to `Enumerable.Where`** (legal, because `IQueryable` IS-A `IEnumerable`) and you have
just downloaded the table. **No error. No warning.**

```csharp
Expression<Func<Book, bool>> predicate = b => b.Price > 100;   //! ✅ ONE WORD. That's the fix.
var result = _dbContext.Books.Where(predicate);                //! → real SQL WHERE
```

**This is precisely why `ABaseRepository.FindUsingPredicateAsync` takes
`Expression<Func<TEntity, bool>>` and not `Func<TEntity, bool>`.** It is not decoration. Change that
one type and the method becomes a table-downloader with an innocent name.

### Bug #3 — multiple enumeration (see Part 3)

### Bug #4 — N+1 *(the death-by-a-thousand-round-trips)*

```csharp
//! ❌ 1 query for the books + 1 query PER book for its reviews = 1 + N queries
List<Book> books = await _dbContext.Books.ToListAsync(ct);
foreach (Book book in books)
{
    Console.WriteLine(book.Reviews.Count);       //! 💥 lazy-load / separate query, once per book
}

//! ✅ ONE query, with a JOIN
List<Book> books = await _dbContext.Books
    .Include(b => b.Reviews)
    .ToListAsync(ct);
```

**This is exactly what `IncludeAggregate()` in `ABaseRepository` exists to prevent** — it guarantees
every read of an aggregate pulls its children in the same round-trip, so no handler can accidentally
trigger N+1.

### Bug #5 — cartesian explosion *(the fix for #4, applied twice, becomes a new bug)*

```csharp
//! ⚠️ Book has 50 Reviews and 3 Authors → the JOIN produces 50 × 3 = 150 rows for ONE book,
//! and every book column is duplicated 150 times across the wire.
var books = _dbContext.Books
    .Include(b => b.Reviews)
    .Include(b => b.Authors);

//! ✅ AsSplitQuery: EF sends 3 separate SELECTs and stitches them together. 
//! Rule of thumb: TWO OR MORE *collection* Includes → AsSplitQuery.
var books = _dbContext.Books
    .Include(b => b.Reviews)
    .Include(b => b.Authors)
    .AsSplitQuery();
```

This is why the `IncludeAggregate` docstring in your `ABaseRepository` says
`//! 2+ collection Includes → cartesian explosion without this`.

---

## Part 7 — Streaming vs buffering (the memory dimension)

Deferred vs immediate is about **time**. Streaming vs buffering is about **memory**. Different axis,
equally important.

```csharp
//! BUFFERED — the whole result set lands in RAM at once.
List<Book> all = await query.ToListAsync(ct);          //! 4M books → 4M objects → OOM

//! STREAMED — one row at a time; only ONE book is in memory at any moment.
await foreach (Book book in query.AsAsyncEnumerable().WithCancellation(ct))
{
    await ProcessAsync(book);                          //! ✅ constant memory, works on 4M rows
}
```

Even inside LINQ-to-Objects, some operators **must** buffer:

| Streaming (constant memory) | Buffering (holds everything) |
|---|---|
| `Where` `Select` `Take` `Skip` `SkipWhile` `TakeWhile` | `OrderBy` `ThenBy` `GroupBy` `Reverse` `ToList` `ToArray` |

Why? `OrderBy` **cannot** yield its first element until it has seen the *last* one — it might be the
smallest. Sorting is inherently a buffering operation. Same for `GroupBy` and `Reverse`.

**Practical rule:** for a big export / batch job, stream (`AsAsyncEnumerable`). For a web request
returning a page of 20 rows, buffer (`ToListAsync`) — and *always* `Skip`/`Take` in SQL, never in RAM.

---

## Part 8 — See the SQL. Always. This is the habit that makes you an expert.

Stop guessing. Three ways to look:

### 1. `ToQueryString()` — instant, no execution, no database needed

```csharp
IQueryable<Book> query = _dbContext.Books.Where(b => b.Price > 100).OrderBy(b => b.Title);
string sql = query.ToQueryString();       //! ← EF Core 5+. Costs nothing. Runs nothing.
Console.WriteLine(sql);
```

**Do this every single time you write a non-obvious predicate.** If the SQL is not what you expected,
you found the bug in 5 seconds instead of 5 hours. If it *throws* "could not be translated" — even
better, you found it before it shipped.

### 2. Log every query EF runs (development only)

```csharp
services.AddDbContext<BookCartDbContext>(options =>
{
    options.UseSqlServer(connectionString);

#if DEBUG
    options.LogTo(Console.WriteLine, LogLevel.Information)
           .EnableSensitiveDataLogging()   //! shows @p0 = 100 instead of @p0 = ?  — NEVER in production
           .EnableDetailedErrors();
#endif
});
```

Now every round-trip prints. **If you see 50 SELECTs on one page load, you have an N+1** (Bug #4) and
you can see it instantly.

### 3. Fail the build on client-side evaluation

```csharp
options.ConfigureWarnings(w =>
    w.Throw(RelationalEventId.MultipleCollectionIncludeWarning));   //! cartesian explosion → exception
```

---

## Part 9 — Now the payoff: why a repository must NEVER return `IQueryable`

Everything above converges here. You now have the tools to see *why* this rule exists, instead of
just obeying it.

```csharp
//! ❌❌ THE ANTI-PATTERN — you will see this in a thousand blog posts. It is wrong.
public interface IBookRepository
{
    IQueryable<Book> GetAll();      //! "so flexible! the caller can compose whatever it needs!"
}
```

It **is** flexible. That is precisely the problem. Five separate disasters, and now you can name
every one of them:

**1. The abstraction is a lie.** The whole promise of a repository (see the companion guide, Part 0)
is *"I am an in-memory collection of aggregates; the database is my secret."* An `IQueryable` **is**
the database — it's a half-built SQL statement with a live `IQueryProvider` attached. You have not
hidden the database; you have handed the caller a loaded gun and called it a collection.

**2. You cannot swap the implementation.** Try backing that interface with Dapper. Or a REST API. Or
an in-memory `List` for tests. You **can't** — the return type contractually demands a LINQ provider.
The interface has welded you to EF Core forever. Dependency Inversion, defeated by a return type.

**3. The caller can write SQL you have never seen — and never reviewed.** A handler can now compose
a 6-table join with a `GroupBy` and a correlated subquery. Your carefully indexed schema now serves
queries written by someone who has never seen the execution plan. Every performance problem is now
everyone's problem, and no one's.

**4. `DbContext` lifetime bugs — the delightful one.** `IQueryable` is *lazy* (Part 3!). So it can
escape the scope that created it:

```csharp
public IQueryable<Book> GetAll() => _dbContext.Books;    //! ❌

//! ... later, in a controller, AFTER the scoped DbContext has been disposed:
var books = repo.GetAll().ToList();                      //! 💥 ObjectDisposedException
```

The query was never executed inside the repository — it was executed *out here*, by which time the
`DbContext` is gone. **This bug is only possible *because* of deferred execution.** You now
understand it from first principles.

**5. Untestable.** Mocking `IQueryable` means mocking `IQueryProvider`. Nobody does this and lives.

### The cure — and it is exactly what your `ABaseRepository` does

**Keep the power. Move the boundary.** `IQueryable` and `Expression<>` are *magnificent* — inside
Infrastructure, where an untranslatable predicate is a bug you can see. They become poison the moment
they cross into Application.

```csharp
//! ═══ Application layer — INTENTION. Executed, materialised, safe. ═══
public interface IBookRepository : IBaseRepository<Book, BookId>
{
    Task<bool> IsIsbnTakenAsync(Isbn isbn, CancellationToken ct = default);
}

//! ═══ Infrastructure — the machinery, PROTECTED, never visible to a handler ═══
internal sealed class BookRepository : ABaseRepository<Book, BookId>, IBookRepository
{
    public Task<bool> IsIsbnTakenAsync(Isbn isbn, CancellationToken ct = default) =>
        AnyAsync(book => book.Isbn.Equals(isbn), ct: ct);   //! ← the protected Expression<> helper
}
```

The handler asks a **question in the language of the business** (*"is this ISBN taken?"*). The
repository answers it. The `Expression`, the `IQueryable`, the `WHERE EXISTS` — all sealed inside
Infrastructure where they belong.

**The rule, stated once:**

> A repository's public surface returns **executed, materialised** results: `Task<T?>`,
> `Task<IReadOnlyList<T>>`, `Task<bool>`, `Task<int>`. **Never `IQueryable<T>`.**

---

## Part 10 — Every design decision in `ABaseRepository`, defended

You asked *why* for each of these. Here is the answer for each, in order.

### Q1 — Why an interface at all? Why not just use the class?

**Dependency Inversion.** The Application layer must not know EF Core exists. The interface lives in
`BookCart.Application` (where it is *used*); the implementation lives in `BookCart.Infrastructure`
(where the detail is). Application depends on the abstraction; Infrastructure depends on the
abstraction. **Neither depends on the other.** That's the arrow-flipping that makes it "Clean"
Architecture.

Concretely, it buys you: (a) handlers you can unit-test with a fake, no database; (b) the freedom to
re-implement `IBookRepository` with Dapper tomorrow without touching one handler.

### Q2 — Why an *abstract class* as well? Isn't the interface enough?

An interface is a **contract** — it says *what*. It cannot hold the *how*. Without a base class,
`GetByIdAsync`, `GetAllAsync`, `IsExistsAsync`, `AddAsync`, `Update`, `Delete`, `DeleteByIdAsync`,
`HardDelete` — **all eight** — would be copy-pasted into `CategoryRepository`, `BookRepository`,
`OrderRepository`, and every future one. Eight methods × N aggregates of identical code.

The abstract class is where the **mechanics** live (the *how* — identical for every aggregate). The
concrete repository holds only the **policy** (the *what* — unique to that aggregate). Your own
`When-To-Abstract-Mechanics-vs-Policy.md` is this exact principle; the repository is a textbook case.

It is `abstract` because **`ABaseRepository<Book, BookId>` is not a thing you should ever be able to
instantiate.** There is no such object as "a base repository." Only `BookRepository` is real.
`abstract` makes the meaningless thing *impossible*, not merely discouraged.

### Q3 — Why does the **base class** implement the interface, instead of the concrete repository?

This is the question you originally asked, and it deserves the precise answer.

C# lets an **inherited public method satisfy an interface member on a derived type.** So this
compiles *even though the base never declares the interface*:

```csharp
//! Option A — base does NOT declare the interface
internal abstract class ABaseRepository<TEntity, TId>            //! no ': IBaseRepository<...>'
{
    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct) => ...;
}

internal sealed class BookRepository
    : ABaseRepository<Book, BookId>, IBookRepository { }         //! ✅ compiles! GetByIdAsync is inherited
```

It works — **by coincidence of matching signatures.** And the coincidence is the bug.

Change one thing on the contract — say `IReadOnlyList<T>` → `IEnumerable<T>`, or drop a
`CancellationToken` default. Now:

- The **base class still compiles perfectly.** It doesn't claim the contract, so nothing is checked.
- **Every single concrete repository breaks**, with
  `'BookRepository' does not implement interface member 'GetAllAsync'` — pointing at a class that
  **does not even contain that method**. Ten aggregates, ten confusing errors, zero of them at the
  real site.

With the base declaring `: IBaseRepository<TEntity, TIdKeyType>`, the compiler verifies the contract
**once, at its source**, and the error appears exactly where the mistake is.

> **The principle:** *if a type fulfils a contract, it should SAY SO.* Never rely on accidental
> signature matching. Say what you mean, and let the compiler check that you meant it.

And the concrete class then **never re-lists** `IBaseRepository` — because `IBookRepository` already
extends it:

```csharp
public interface IBookRepository : IBaseRepository<Book, BookId> { /* + book-only methods */ }

internal sealed class BookRepository : ABaseRepository<Book, BookId>, IBookRepository { }
//                                     └── the HOW (mechanics)   └── the WHAT (contract)
```

### Q4 — Why are `Query`, `FindUsingPredicateAsync`, `AnyAsync` **`protected`** and not on the interface?

Because of **everything in Parts 1–9.** Put `Expression<Func<TEntity, bool>>` on the Application
interface and you have handed handlers the ability to write SQL they cannot see, cannot review, and
cannot be sure translates. The failure mode is a **runtime** exception in production, not a compile
error.

`protected` gives you the exact split you want:

- **A derived repository** (Infrastructure — the layer that is *allowed* to know about EF) composes
  with them freely.
- **A command handler** (Application — holding only `IBookRepository`) **cannot even see them.** Not
  "shouldn't." *Cannot.* The compiler enforces the architecture.

That is the difference between a **guideline** (which people violate under deadline) and an
**invariant** (which people cannot violate). Always prefer the invariant.

### Q5 — Why does `Delete` return `Result`, when the companion guide's Rule 4 says repositories return data, not `Result`?

**Good catch — the two documents genuinely disagree, and you should know why.**

Rule 4 is about *queries*: `GetByIdAsync` returns `Book?`, **not** `Result<Book>`. "Not found" is not
an error the *repository* can classify — only the handler knows whether a missing book is a 404, an
empty state, or perfectly fine. Wrapping it in `Result` at the repository level is presumptuous.
**That rule stands.**

`Delete` is the deliberate exception, and here is the argument: it is **not a query — it invokes a
domain method that is allowed to refuse.**

```csharp
public Result Delete(TEntity entity)
{
    if (entity is IDeletable deletable)
        return deletable.MarkAsDeleted();   //! ← the DOMAIN decides. It can say "AlreadyDeleted".
    Set.Remove(entity);
    return Result.Success();
}
```

`AMasterEntity.MarkAsDeleted()` returns `Result` — it can refuse with `EntityStateErrors.AlreadyDeleted`
(→ 409 Conflict). The repository is **relaying a domain verdict it did not invent.** Its only
alternatives are to swallow the refusal (turning a 409 into a silent success — a real bug) or to
throw (using exceptions for expected control flow — the exact thing your `Result` pattern exists to
abolish).

> **The refined rule:** a repository does not *invent* `Result`s. But when it calls a domain method
> that returns one, it **must not swallow it**. Relaying ≠ inventing.

---

## Part 11 — Three real aggregates, fully implemented

`Category` exists in your domain today. `Book` and `Order` are sketched here to show the two cases
the base class must handle: **a simple aggregate**, **an aggregate with children**, and **an
aggregate with children + a custom query**.

### Aggregate 1 — `Category` (no navigations — the simple case)

```csharp
//! ═══ Application/Common/Abstractions/Data/Repositories/ICategoryRepository.cs ═══
public interface ICategoryRepository : IBaseRepository<Category, CategoryId>
{
    Task<bool> ExistsWithNameAsync(CategoryName name, CancellationToken ct = default);
    Task<Category?> GetByNameAsync(CategoryName name, CancellationToken ct = default);
}

//! ═══ Infrastructure/Persistence/Repositories/CategoryRepository.cs ═══
internal sealed class CategoryRepository : ABaseRepository<Category, CategoryId>, ICategoryRepository
{
    public CategoryRepository(BookCartDbContext dbContext) : base(dbContext) { }

    //! NO IncludeAggregate override — Category has no child collections. The base default is correct.

    public Task<bool> ExistsWithNameAsync(CategoryName name, CancellationToken ct = default) =>
        AnyAsync(category => category.Name.Equals(name), ct: ct);          //! → SELECT EXISTS(...)

    public Task<Category?> GetByNameAsync(CategoryName name, CancellationToken ct = default) =>
        FindOneUsingPredicateAsync(category => category.Name.Equals(name), tracked: true, ct: ct);
}
```

**Eight CRUD methods inherited. Two lines of real code written.** That is the abstract class earning
its keep.

> ⚠️ **Value-object comparison rule:** with a `HasConversion(...)` value converter, EF sees `Name` as
> a **scalar column**. So compare the **whole value object** (`category.Name.Equals(name)`) — never
> `category.Name.Value == name.Value`. EF cannot see *inside* a converted type, and that will fail to
> translate. Verify with `ToQueryString()` (Part 8) the first time you write one.

### Aggregate 2 — `Book` (one child collection — needs `IncludeAggregate`)

```csharp
public interface IBookRepository : IBaseRepository<Book, BookId>
{
    Task<bool> IsIsbnTakenAsync(Isbn isbn, CancellationToken ct = default);
    Task<IReadOnlyList<Book>> GetByCategoryAsync(CategoryId categoryId, CancellationToken ct = default);
}

internal sealed class BookRepository : ABaseRepository<Book, BookId>, IBookRepository
{
    public BookRepository(BookCartDbContext dbContext) : base(dbContext) { }

    /*
        //! THE OVERRIDE THAT MATTERS. A Book WITHOUT its Reviews is not a Book — it is a half-object,
        //! and any invariant Book enforces across Reviews (e.g. "average rating") is unenforceable.
        //! Every read in the base class routes through this hook, so it is IMPOSSIBLE to load a
        //! half-Book. That is Bug #4 (N+1) made structurally unreachable.
    */
    protected override IQueryable<Book> IncludeAggregate(IQueryable<Book> query) =>
        query.Include(book => book.Reviews);

    public Task<bool> IsIsbnTakenAsync(Isbn isbn, CancellationToken ct = default) =>
        AnyAsync(book => book.Isbn.Equals(isbn), ct: ct);
        //! ↑ AnyAsync passes includeAggregate: false internally — joining Reviews to answer a BOOL
        //!   would be pure waste. The base already knows this. You get it for free.

    public async Task<IReadOnlyList<Book>> GetByCategoryAsync(
        CategoryId categoryId, CancellationToken ct = default) =>
        await FindUsingPredicateAsync(book => book.CategoryId.Equals(categoryId), ct: ct);
}
```

### Aggregate 3 — `Order` (two collections → `AsSplitQuery`, + a real business query)

```csharp
public interface IOrderRepository : IBaseRepository<Order, OrderId>
{
    Task<IReadOnlyList<Order>> GetPendingForCustomerAsync(CustomerId customerId, CancellationToken ct = default);
    Task<int> CountPlacedSinceAsync(DateTime since, CancellationToken ct = default);
}

internal sealed class OrderRepository : ABaseRepository<Order, OrderId>, IOrderRepository
{
    public OrderRepository(BookCartDbContext dbContext) : base(dbContext) { }

    /*
        //! TWO collection Includes → cartesian explosion (Bug #5): 20 Lines × 5 Payments = 100 rows
        //! for ONE order, every order column duplicated 100 times across the wire.
        //! AsSplitQuery → 3 clean SELECTs, stitched by EF. THIS is why the hook is virtual.
    */
    protected override IQueryable<Order> IncludeAggregate(IQueryable<Order> query) =>
        query.Include(order => order.Lines)
             .Include(order => order.Payments)
             .AsSplitQuery();

    public async Task<IReadOnlyList<Order>> GetPendingForCustomerAsync(
        CustomerId customerId, CancellationToken ct = default) =>
        await FindUsingPredicateAsync(
            order => order.CustomerId.Equals(customerId) && order.Status == OrderStatus.Pending,
            tracked: false,          //! a READ → no tracking → ~2x faster, less memory
            ct: ct);

    public Task<int> CountPlacedSinceAsync(DateTime since, CancellationToken ct = default) =>
        CountAsync(order => order.PlacedAt >= since, ct: ct);   //! → SELECT COUNT(*). One scalar.
}
```

**Look at what you did NOT write in any of the three:** `GetByIdAsync`, `GetAllAsync`,
`IsExistsAsync`, `AddAsync`, `Update`, `Delete`, `DeleteByIdAsync`, `HardDelete`. Eight methods,
three aggregates — **24 methods you did not write, cannot get wrong, and can fix in one place.**

---

## Part 12 — Dependency Injection, done right (with the trap)

`BookCart.Infrastructure/DependencyInjection.cs`:

```csharp
private static IServiceCollection AddRepositoriesAndUnitOfWork(this IServiceCollection services)
{
    //! Scoped = one instance per HTTP request. Correct: it must share the request's DbContext,
    //! because the Unit of Work commits everything staged during THAT request, atomically.
    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IBookRepository,     BookRepository>();
    services.AddScoped<IOrderRepository,    OrderRepository>();

    /*
        //! 🚨🚨 THE TRAP — the most expensive two lines in this entire document.
        //!
        //!    services.AddScoped<IUnitOfWork, BookCartDbContext>();     // ❌ LOOKS RIGHT. IS A DISASTER.
        //!
        //! That registers a SECOND service descriptor for BookCartDbContext. The DI container has NO
        //! idea it should reuse the one from AddDbContext<>() — so it CONSTRUCTS A SECOND INSTANCE.
        //!
        //! Result: your repositories stage Add/Update/Delete on DbContext **A**, and then
        //! SaveChangesAsync() commits DbContext **B** — which is empty.
        //!
        //! EVERY WRITE IN YOUR APPLICATION SILENTLY DOES NOTHING. No exception. No error. No log.
        //! SaveChangesAsync even returns 0 — and nobody checks the return value.
        //!
        //! ✅ THE FIX: resolve the SAME instance the DbContext registration already created.
    */
    services.AddScoped<IUnitOfWork>(serviceProvider =>
        serviceProvider.GetRequiredService<BookCartDbContext>());

    return services;
}
```

Then call it from `AddPersistenceServices`:

```csharp
services
    .AddBookCartDbContextService(connectionString)
    .AddConnectionFactoryForDapper(connectionString)
    .AddRepositoriesAndUnitOfWork();          //! ← add this line
```

### Why `internal` repositories still register fine

`CategoryRepository` is `internal` — invisible outside `BookCart.Infrastructure`. But
`DependencyInjection.cs` **lives inside that same assembly**, so it can see it. The Web layer only
ever sees the **public interface**. That is exactly right: **the Web project is physically incapable
of `new CategoryRepository(...)`, or of even naming the type.** The architecture is enforced by the
compiler, not by a code-review comment.

---

## Part 13 — Using it for real: the two flows

### Flow A — a COMMAND (write side → EF + repository + Unit of Work)

```csharp
internal sealed class CreateCategoryCommandHandler
    : IRequestHandler<CreateCategoryCommand, Result<CategoryId>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;   //! ← the INTERFACE. Zero knowledge of EF Core.
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CategoryId>> Handle(CreateCategoryCommand command, CancellationToken ct)
    {
        //! 1. Build the value objects — the DOMAIN validates, not the handler.
        Result<CategoryName> nameResult = CategoryName.Create(command.Name);
        if (nameResult.IsFailure)
        {
            return nameResult.Errors;
        }

        //! 2. Business rule, asked in BUSINESS language. Behind it: SELECT EXISTS(...).
        //!    The handler does not know, and MUST not know, that SQL exists.
        if (await _categoryRepository.ExistsWithNameAsync(nameResult.Value, ct))
        {
            return CategoryErrors.NameAlreadyTaken;      //! → 409 Conflict
        }

        //! 3. The DOMAIN creates the aggregate (and raises CategoryCreatedDomainEvent).
        Result<Category> categoryResult = Category.Create(command.Name, command.Description);
        if (categoryResult.IsFailure)
        {
            return categoryResult.Errors;
        }

        //! 4. STAGE it. Nothing has touched the database yet — this only mutates the change tracker.
        await _categoryRepository.AddAsync(categoryResult.Value, ct);

        //! 5. COMMIT. ONE transaction. THIS is the only line that writes.
        //!    The audit interceptor stamps CreatedAt/CreatedBy here; domain events dispatch here.
        await _unitOfWork.SaveChangesAsync(ct);

        return categoryResult.Value.Id!;
    }
}
```

**Read step 4 and 5 again.** `AddAsync` does not insert. `SaveChangesAsync` inserts. If this handler
added a `Category` *and* an `Outbox` row *and* updated a counter, **all three would commit atomically
or none would.** That is the Unit of Work, and it is why the repository must never call
`SaveChangesAsync` itself.

### Flow B — a DELETE (showing the `Result` relay from Part 10, Q5)

```csharp
public async Task<Result> Handle(DeleteCategoryCommand command, CancellationToken ct)
{
    Result<CategoryId> idResult = CategoryId.From(command.Id);
    if (idResult.IsFailure)
    {
        return idResult.Errors;
    }

    //! DeleteByIdAsync: loads (tracked, filters applied) → dispatches to the domain's MarkAsDeleted()
    //!   → not found                → Category.NotFound   (404)
    //!   → found, already deleted   → Entity.AlreadyDeleted (409)   ← the domain REFUSED. Relayed, not swallowed.
    //!   → found, deleted OK        → Success + CategoryDeletedDomainEvent raised
    Result deleteResult = await _categoryRepository.DeleteByIdAsync(idResult.Value, ct);
    if (deleteResult.IsFailure)
    {
        return deleteResult.Errors;      //! ← if the repo had swallowed this, a 409 would become a 200.
    }

    await _unitOfWork.SaveChangesAsync(ct);   //! the soft delete is an UPDATE. It commits HERE.
    return Result.Success();
}
```

### Flow C — a QUERY (read side → **skip the repository entirely**)

This is the part most people get wrong, and it is why your project carries **both** EF Core and Dapper.

```csharp
//! ❌ WRONG — do NOT use the repository for a screen.
var all = await _categoryRepository.GetAllAsync(ct);          //! loads FULL aggregates, tracked,
var page = all.Skip(20).Take(20)                              //! then PAGES IN RAM (Bug #1!)
              .Select(c => new CategoryDto(c.Id!.Value, c.Name.Value));

//! ✅ RIGHT — a read model. Dapper. Straight to a DTO. No aggregate, no tracker, no EF.
internal sealed class GetCategoriesQueryHandler
    : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery query, CancellationToken ct)
    {
        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();

        const string sql = """
            SELECT   Id, Name, CategoryDescription AS Description
            FROM     application.Categories
            WHERE    IsDeleted = 0
            ORDER BY Name
            OFFSET   @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var categories = await connection.QueryAsync<CategoryDto>(
            sql, new { Offset = (query.Page - 1) * query.PageSize, query.PageSize });

        return categories.AsList();
    }
}
```

> ### 🧠 The CQRS split, in one line
>
> **WRITE side** → repository + EF + aggregates + Unit of Work. *Correctness* is what matters:
> invariants, domain events, atomic commits.
> **READ side** → Dapper + hand-written SQL + flat DTOs. *Speed* is what matters: no tracker, no
> aggregate, no `Include`, exactly the columns the screen shows.
>
> **A repository that also serves screens will be slowly tortured into an ORM.** Don't do it. The
> `GetAllAsync` on `IBaseRepository` is a convenience for small write-side lookups — **not** a
> licence to build UIs on it. That is exactly what its `//! Avoid on large tables` comment means.

---

## Part 14 — Exercises

> Do them **in order**. Write the answer down *before* scrolling to Part 15. The ones marked 🔥 are
> the ones that actually separate people who "know about" this from people who *know* it.

**E1.** Does this hit the database? If so, when — and how many times?
```csharp
var q = _dbContext.Books.Where(b => b.Price > 100);
var q2 = q.OrderBy(b => b.Title);
var q3 = q2.Take(5);
```

**E2.** 🔥 What is the difference in emitted SQL between these two? Why?
```csharp
var a = _dbContext.Books.Where(b => b.Price > 100).ToList();
var b = _dbContext.Books.ToList().Where(b => b.Price > 100).ToList();
```

**E3.** Why does this compile but perform terribly?
```csharp
IEnumerable<Book> books = _dbContext.Books;
var expensive = books.Where(b => b.Price > 100).ToList();
```

**E4.** 🔥 Why does this one silently load the whole table, even though the source is a `DbSet`?
```csharp
Func<Book, bool> predicate = b => b.Price > 100;
var result = _dbContext.Books.Where(predicate).ToList();
```

**E5.** How many round-trips? How do you make it one?
```csharp
var q = _dbContext.Books.Where(b => b.Price > 100);
Console.WriteLine(q.Count());
Console.WriteLine(q.First().Title);
foreach (var b in q) { Console.WriteLine(b.Title); }
```

**E6.** 🔥 `IQueryable<T>` inherits `IEnumerable<T>`. Name the specific bug this inheritance enables,
and explain why the compiler cannot catch it.

**E7.** Why can't EF Core translate `.Where(b => MyRule(b))`? Answer in terms of *delegates vs
expression trees*, not "because EF doesn't support it."

**E8.** 🔥 Your teammate adds `IQueryable<Book> Query()` to `IBookRepository` "for flexibility."
Give **four** distinct reasons to reject it in code review. (Part 9 has five.)

**E9.** `ABaseRepository.AnyAsync` calls `Query(tracked: false, includeAggregate: false, ...)`.
Justify **both** flags. What breaks if each is flipped?

**E10.** 🔥 A `Book` has 30 `Reviews` and 4 `Authors`. Your `IncludeAggregate` does
`.Include(Reviews).Include(Authors)` with no `AsSplitQuery()`. How many rows come back for **one**
book, and what exactly is duplicated?

**E11.** 🔥 Why does `ABaseRepository.Update()` check `State == EntityState.Detached` first? What
concretely goes wrong if you just always call `Set.Update(entity)`?

**E12.** 🔥🔥 A colleague registers `services.AddScoped<IUnitOfWork, BookCartDbContext>();`. Every
write in the app silently does nothing, with no exception. **Explain the exact mechanism.**

---

## Part 15 — Solutions

**S1.** **Zero round-trips.** Every operator (`Where`, `OrderBy`, `Take`) returns `IQueryable<T>` —
lazy, tree-building. None is in the trigger list (Part 3). `q3` is a *query*, not data. Add
`.ToList()` and you get **one** round-trip with all three baked into one SQL statement.

**S2.** `a` → `SELECT ... FROM Books WHERE Price > @p0`. The filter is in SQL; **only matching rows
cross the wire.**
`b` → `SELECT * FROM Books` (**every row**, materialised into a `List`), then a **RAM** filter via
`Enumerable.Where`. The `.ToList()` **teleported you from Universe 2 into Universe 1** (Part 4). Same
result, catastrophically different cost. **This is Bug #1.**

**S3.** The **static type** of `books` is `IEnumerable<Book>` (Part 5). So the compiler binds to
`Enumerable.Where`, which takes a `Func<>`. `SELECT * FROM Books` streams the whole table; the filter
runs in your process. The `DbSet` was *capable* of SQL translation — **the type annotation threw that
capability away.**

**S4.** 🔥 The heart of it. `Queryable.Where` requires `Expression<Func<Book,bool>>`. You handed it a
`Func<Book,bool>` — **a delegate, not a tree.** No matching overload… except that `IQueryable<Book>`
**IS-A** `IEnumerable<Book>`, so overload resolution happily falls back to `Enumerable.Where`.
Full table download. **No warning, no error, no cast.** Fix: declare it
`Expression<Func<Book, bool>> predicate = ...`. **One word.**

**S5.** **Three round-trips** — `Count()`, `First()`, and the `foreach` each re-execute the tree
(Part 3). Fix: `List<Book> books = await q.ToListAsync(ct);` → **one** round-trip, then use
`books.Count`, `books[0]`, and `foreach (var b in books)` — all free, all in RAM.

**S6.** 🔥 It enables **silent degradation from SQL to in-memory** (Bugs #1 and #2). The compiler
cannot catch it because **it is not an error** — it is a perfectly legal, intentional part of the type
system (you *must* be able to `foreach` an `IQueryable`). Assigning `IQueryable` to `IEnumerable` is
a widening conversion, exactly like assigning a `Dog` to an `Animal`. The type system is working as
designed; it just cannot read your *intent*.

**S7.** A `Func<>` is **compiled IL — a black box**. EF can *invoke* it; it cannot *read* it. To emit
SQL, EF must **inspect** the predicate: which property, which operator, which constant. Only an
`Expression<Func<>>` — code-as-data — permits inspection. `MyRule(b)` appears in the tree as a
`MethodCallExpression` to a method EF has no SQL mapping for, so it throws. **Translation requires
readability; delegates are unreadable.** (Part 2.)

**S8.** Any four of: **(1)** it leaks EF Core into Application — Dependency Inversion is dead;
**(2)** you can no longer swap the implementation (Dapper/API/fake) — the return type welds you to a
LINQ provider; **(3)** handlers can compose arbitrary un-reviewed SQL against your indexed schema;
**(4)** deferred execution lets the query escape the scope and blow up with `ObjectDisposedException`
after the `DbContext` is disposed; **(5)** it is untestable without mocking `IQueryProvider`.

**S9.** `tracked: false` — the answer is a `bool`; a change-tracker snapshot would be built for
entities you immediately discard. Flip it → pointless CPU + memory per call, and you pollute the
tracker with entities you never intended to mutate. `includeAggregate: false` — you would `JOIN`
child tables **to compute a boolean**. Flip it → every existence check drags every child row across
the network to answer *yes/no*. **Both flags exist because the shape of the answer (`bool`) tells you
what work is waste.**

**S10.** 🔥 **120 rows** (30 × 4) for one book. The `Book` columns are duplicated **120 times**; each
`Review` row is duplicated 4× (once per author); each `Author` row 30× (once per review). That is the
**cartesian explosion** (Bug #5). `.AsSplitQuery()` → 3 SELECTs, `1 + 30 + 4 = 35` rows total.

**S11.** 🔥 The normal flow is `GetByIdAsync` → domain method → `SaveChangesAsync`, and that entity is
**tracked**. EF's snapshot comparer already knows precisely which columns changed and emits a
**targeted** `UPDATE [Categories] SET [Name] = @p0`. Calling `Set.Update()` on a tracked entity flips
**every** property to `Modified` → a **full-row UPDATE** that rewrites columns nobody touched, fights
concurrency tokens, and re-marks the whole navigation graph as `Modified`. `Update()` is *only* right
for a **detached** entity (rebuilt from a DTO, or from another scope), where nothing is watching it.
Hence the guard.

**S12.** 🔥🔥 `AddScoped<IUnitOfWork, BookCartDbContext>()` creates a **second service descriptor**.
The container has no idea it should reuse the instance from `AddDbContext<BookCartDbContext>()`, so
when `IUnitOfWork` is requested it **constructs a brand-new `BookCartDbContext`**. Now the scope holds
**two** contexts. Repositories are injected with context **A** and stage `Add`/`Update`/`Delete` into
**A's** change tracker. The handler then calls `_unitOfWork.SaveChangesAsync()` — on context **B**,
whose change tracker is **empty**. B dutifully saves nothing, returns `0`, and no one checks the
return value. **Every write is a silent no-op.** Fix:
`services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BookCartDbContext>());` — resolve, don't
re-register.

---

## Part 16 — Cheat sheet

### The decision table

| You are… | Use | Because |
|---|---|---|
| Querying a database | `IQueryable<T>` | filter/sort/page runs **in SQL** |
| Working with data already in RAM | `IEnumerable<T>` | no provider, no translation, full C# |
| Passing a predicate **to a database** | `Expression<Func<T,bool>>` | EF must **read** it |
| Passing a predicate **to a `List`** | `Func<T,bool>` | it only needs to **run** it |
| Returning from a **repository method** | `Task<T?>` `Task<IReadOnlyList<T>>` `Task<bool>` | executed, materialised, safe |
| Returning `IQueryable` from a repository | ❌ **never** | Part 9, all five reasons |

### Red flags in code review

```csharp
IEnumerable<T> Something(...)   // near a DbContext?              → 🚩 silent full table scan
IQueryable<T>  Something(...)   // on an Application interface?    → 🚩 leaked abstraction
Func<T, bool>  predicate        // passed to .Where() on a DbSet?  → 🚩 Bug #2, table download
.ToList().Where(...)            // materialise-then-filter         → 🚩 Bug #1, backwards
.Include().Include()            // 2+ collections, no AsSplitQuery → 🚩 Bug #5, cartesian
foreach (...) { x.Children... } // navigation inside a loop        → 🚩 Bug #4, N+1
AddScoped<IUnitOfWork, DbCtx>() // re-registering the impl type    → 🚩 E12, all writes vanish
```

### The five sentences that are the whole guide

1. **Both are deferred.** The difference is **where your lambda runs** — RAM or the database.
2. **`Func` is code you can RUN; `Expression` is code you can READ.** SQL translation needs *reading*.
3. **`IQueryable` IS-A `IEnumerable`** — so it degrades to a table scan **silently**, with no error.
4. **Narrow in SQL, refine in RAM.** Never the reverse.
5. **The repository's public surface is executed and materialised. `IQueryable` never crosses into
   Application.**

---

## Part 17 — Your 14-day mastery plan

You said you want to master this in two weeks. Here is the actual path. **Do not skip the "prove it"
column — reading is not learning. Typing it and watching the SQL is learning.**

| Day | Study | Prove it (this is the part that matters) |
|---|---|---|
| **1** | Part 0–1. The myth. The hierarchy. | Run the `Console.WriteLine` deferred-execution demo from Part 0 on a plain `List`. **Watch `IEnumerable` be lazy with your own eyes.** |
| **2–3** | Part 2. Expression trees. **The core.** | Build `Expression<Func<Book,bool>> e = b => b.Price > 100;` and print `e.Body.NodeType`, `((BinaryExpression)e.Body).Left`, `.Right`. Then call `e.Compile()` and invoke it. |
| **4** | Part 3. Deferred execution + triggers. | Memorise the trigger table. Then compose a 4-stage query and call `ToQueryString()` **before** executing. Prove no SQL ran. |
| **5** | Part 4–5. Two universes. Overload resolution. | Write E3 and E4. Run both. Log the SQL. **See the full table scan happen.** This is the day it clicks. |
| **6–7** | Part 6. The five bugs. | **Reproduce all five in a scratch project.** Deliberately cause an N+1, then watch 50 SELECTs print in the log. You will never write one again. |
| **8** | Part 7–8. Streaming. `ToQueryString`. Logging. | Turn on `LogTo(Console.WriteLine)` in BookCart **permanently** for dev. |
| **9** | Part 9. Why no `IQueryable` in the repo. | Re-read `ABaseRepository.cs` end to end. Every comment should now read as *obvious*. |
| **10** | Part 10. The design decisions. | Explain Q3 (why the **base** implements the interface) out loud, from memory, to a rubber duck. |
| **11–12** | Part 11–12. Build it. | Implement `CategoryRepository` + `CategoryConfiguration` + DI **by hand**, no copy-paste. Run the migration. |
| **13** | Part 13. The flows. | Write `CreateCategoryCommandHandler` and a Dapper `GetCategoriesQueryHandler`. Feel the CQRS split. |
| **14** | Part 14. Exercises. | Do **all 12** closed-book. Any you miss → go back to that Part. |

### How you'll know you've mastered it

You can look at any line of LINQ and answer instantly, without running it:

1. **Has this executed yet?** (trigger list)
2. **Where will this lambda run — SQL or RAM?** (static type of the source)
3. **How many round-trips?** (count the triggers)
4. **What SQL comes out?** (and you check with `ToQueryString()` rather than trusting yourself)

When those four answers are automatic, you are done. **You will be faster than most seniors**, because
most seniors have never actually looked at an expression tree.

---

### TL;DR

> **Deferred execution is not the difference — both are lazy.** The difference is that
> `Expression<Func<>>` is **code as data**, so EF can *read* your lambda and rewrite it as SQL, while
> `Func<>` is a compiled black box that can only be *run*, in RAM, on rows already downloaded. Because
> `IQueryable` **IS-A** `IEnumerable`, that catastrophic downgrade happens **silently**, with no
> compiler error. So: keep `IQueryable` and `Expression<>` **inside Infrastructure** (`protected` on
> `ABaseRepository`), and let the repository's public surface hand back only **executed, materialised**
> results. The abstract class holds the **mechanics** every aggregate shares; the interface holds the
> **contract** Application depends on; the base declares the interface so the **compiler** — not a code
> reviewer — verifies it, once, at the source.
