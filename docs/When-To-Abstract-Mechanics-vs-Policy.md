# When to Abstract, and When to STOP — Mechanics vs Policy (the capstone)

> **The trilogy:**
> 1. `AStronglyTypedId` — abstracted the id mechanics. ✅ Correct.
> 2. `ASingleValueObject<T>` — abstracted the single-value VO mechanics. ✅ Correct.
> 3. **"Let's also make a base `Create` factory for all entities (Category, Product…)."** ❌ **Stop.**
>
> This doc explains *why the third one is a trap*, gives you a **decision framework** you can run on
> any future "should I abstract this?" question, and shows the **composition** techniques that give
> you the reuse you actually want — without a god-base-class.

---

## Part 0 — The direct verdict

> **Do NOT create an abstract/interface base that owns entity `Create` factories.**
>
> An entity's `Create` is **pure policy** — different parameters, different cross-field invariants,
> different domain events per aggregate. There is **no shared mechanic** in its body to extract.
> The mechanics entities *do* share (identity, equality, the domain-event list) are **already**
> centralized — in `ABaseEntity` / `AMasterEntity`. That job is done. The factory is meant to be
> per-aggregate, and keeping it that way is the *correct* design, not a missed abstraction.

Everything below proves this and shows what to build instead.

---

## Part 1 — The one framework that answers all three cases

Before abstracting *anything*, run it through this. Two axes, four quadrants:

```
                    IDENTICAL across types?              DIFFERENT across types?
                 (byte-for-byte the same body)        (the body varies per type)
              ┌─────────────────────────────────┬──────────────────────────────────┐
  MECHANICS   │  ✅ ABSTRACT via inheritance     │   (rare — if a mechanic varies,   │
  (how it     │     (abstract base class)        │    it's probably policy in disguise)│
  works)      │  → AStronglyTypedId.Value        │                                    │
              │  → ASingleValueObject.ToString   │                                    │
              ├─────────────────────────────────┼──────────────────────────────────┤
  POLICY      │  (rare — identical rules usually │  ❌ DO NOT inherit.                │
  (what it    │   means it's really one type)    │  ✅ Keep local (SRP).             │
  means:      │                                  │  ✅ Share cross-cutting PLUMBING  │
  rules,      │                                  │     via COMPOSITION (a helper),   │
  events)     │                                  │     never a base class.           │
              └─────────────────────────────────┴──────────────────────────────────┘
```

Now drop your three cases onto it:

| Case | What you wanted to share | Bucket | Verdict |
|------|--------------------------|--------|---------|
| Strongly-typed IDs | `Value`, `ToString`, format-check *mechanic* | Mechanics / Identical | ✅ Abstract base (`AStronglyTypedId`) |
| Single-value VOs | `Value`, `ToString`, equality *mechanic* | Mechanics / Identical | ✅ Abstract base (`ASingleValueObject<T>`) |
| **Entity factories** | **`Create` — params, invariants, events** | **Policy / Different** | ❌ **No base. Compose instead.** |

> **The law:** *Inheritance is for identical mechanics. Policy that differs is shared — if at all —
> by composition.* "Prefer composition over inheritance" isn't a slogan here; it's the exact tool
> switch this table dictates.

---

## Part 2 — Prove it: put two entity factories side by side

Look at `Category.Create` and a future `Product.Create`. Ask: **what line could live in a shared base?**

```csharp
// Category — a tiny aggregate
public static Result<Category> Create(string name, string? description)
{
    var errors = new List<Error>();

    Result<CategoryName> nameR = CategoryName.Create(name);
    if (nameR.IsFailure) errors.AddRange(nameR.Errors);

    CategoryDescription? desc = null;
    if (description is not null)
    {
        Result<CategoryDescription> descR = CategoryDescription.Create(description);
        if (descR.IsFailure) errors.AddRange(descR.Errors);
        else desc = descR.Value;
    }

    if (errors.Count > 0) return errors;

    var category = new Category(CategoryId.New(), nameR.Value, desc);
    category.RaiseDomainEvent(new CategoryCreatedDomainEvent(category.Id!));
    return category;
}
```

```csharp
// Product — a bigger aggregate with cross-field rules
public static Result<Product> Create(
    string title, decimal price, int stock, string isbn, CategoryId categoryId)
{
    var errors = new List<Error>();

    Result<ProductTitle> titleR = ProductTitle.Create(title);
    if (titleR.IsFailure) errors.AddRange(titleR.Errors);

    Result<Price> priceR = Price.Create(price);
    if (priceR.IsFailure) errors.AddRange(priceR.Errors);

    Result<StockQuantity> stockR = StockQuantity.Create(stock);
    if (stockR.IsFailure) errors.AddRange(stockR.Errors);

    Result<Isbn> isbnR = Isbn.Create(isbn);
    if (isbnR.IsFailure) errors.AddRange(isbnR.Errors);

    // ← CROSS-FIELD invariant no single VO can know:
    if (priceR.IsSuccess && priceR.Value.Value == 0 && stock > 0)
        errors.Add(ProductErrors.FreeProductCannotHaveStock);

    if (errors.Count > 0) return errors;

    var product = new Product(
        ProductId.New(), titleR.Value, priceR.Value, stockR.Value, isbnR.Value, categoryId);
    product.RaiseDomainEvent(new ProductCreatedDomainEvent(product.Id!));
    return product;
}
```

**Now try to hoist a base `Create`:**
- Return types differ (`Result<Category>` vs `Result<Product>`).
- Parameter lists differ completely (arity, types, order).
- The VOs constructed differ. The cross-field invariants differ (`Product` has one; `Category` has none).
- The events differ (`CategoryCreatedDomainEvent` vs `ProductCreatedDomainEvent`).
- The private constructor called differs.

**The only thing repeated is the _shape_:** `collect errors → validate each VO → check invariants →
if errors return them → construct → raise event → return`. That shape is a **pattern you follow**
(documented in the Rich Domain playbook), **not** code you inherit. The moment you try to force it
into a base you must pass in *everything that differs* as parameters/delegates — at which point the
"base" is just a confusing wrapper around what you already wrote.

---

## Part 3 — Why C# itself refuses to help you here

Even if you wanted to, the language pushes back — and understanding *why* cements the concept:

1. **`static` factories aren't polymorphic.** `Create` is `static`; static methods are not inherited
   as overridable members. A base can't declare a `Create` that subclasses "override" the normal way.
2. **A base cannot call a derived `private` constructor.** Construction is the derived type's secret;
   the base has no access. So a base factory can only build the subtype if the subtype *hands it a
   factory delegate* — you'd be injecting the very thing that varies.
3. **`static abstract` interface members (C# 11+) exist — but don't help.** You *could* write
   `interface IFactory<TSelf, TArgs> { static abstract Result<TSelf> Create(TArgs a); }`. But the
   **body is still 100% per-entity**; the interface centralizes *a signature you can't even keep
   uniform* (each entity's args differ), buying nothing while adding CRTP noise. Correctly rejected.

> Language friction is a design smell detector. When the compiler makes an abstraction awkward, it's
> often telling you the abstraction is wrong. Here it's screaming *"this is policy, leave it local."*

---

## Part 4 — What you SHOULD share for entities (the legitimate reuse)

You *do* get reuse — just from the right tools. Three of them:

### 4.1 — Mechanics: already centralized in `ABaseEntity` / `AMasterEntity` ✅

Stop and appreciate: the entity equivalent of `AStronglyTypedId` **already exists**. `ABaseEntity`
gives every entity its `Id`, equality/`==`, `GetHashCode`, and the `_domainEvents` list +
`RaiseDomainEvent`. `AMasterEntity` adds the audit/soft-delete columns. **The identical mechanics
are done.** The factory was never part of that set — and shouldn't be.

### 4.2 — Cross-cutting plumbing: a composition helper (optional, Rule of Three)

The *only* thing that genuinely repeats across factory **bodies** is the error-accumulation dance:
`new List<Error>()` … `if (r.IsFailure) errors.AddRange(r.Errors)` … `if (errors.Count > 0) return`.
That's plumbing, not policy — so share it by **composition**, a helper you *use*, not inherit:

```csharp
namespace BookCart.Domain.Common.Result;

/*
 *? A tiny accumulator that de-noises factory bodies. It is COMPOSED (a local variable), never a
 *? base class — so it serves entities AND value objects without coupling them into a hierarchy.
 */
public sealed class ErrorCollector
{
    private readonly List<Error> _errors = [];

    public bool HasErrors => _errors.Count > 0;
    public IReadOnlyList<Error> Errors => _errors;

    public ErrorCollector Add(Error error)
    {
        _errors.Add(error);
        return this;
    }

    /*
     *? Absorbs a nested Result's errors and hands back its value when successful.
     *? Returns TRUE when it FAILED — reads as a guard: "if it failed, move on."
     */
    public bool Failed<T>(Result<T> result, out T value)
    {
        if (result.IsFailure)
        {
            _errors.AddRange(result.Errors);
            value = default!;
            return true;
        }

        value = result.Value;
        return false;
    }
}
```

`Category.Create` then reads as intent, not plumbing:

```csharp
public static Result<Category> Create(string name, string? description)
{
    var errors = new ErrorCollector();

    errors.Failed(CategoryName.Create(name), out var vName);

    CategoryDescription? vDesc = null;
    if (description is not null && !errors.Failed(CategoryDescription.Create(description), out var d))
        vDesc = d;

    if (errors.HasErrors) return errors.Errors.ToList();

    var category = new Category(CategoryId.New(), vName, vDesc);
    category.RaiseDomainEvent(new CategoryCreatedDomainEvent(category.Id!));
    return category;
}
```

> **Note the difference from a base class:** `ErrorCollector` is a *local variable* the factory
> **has**, not a parent it **is**. Category doesn't become a subtype of anything; it just borrows a
> tool. That's composition — flexible, testable, no inheritance coupling. Introduce it once you feel
> the plumbing three times (Rule of Three); before that, the plain `List<Error>` is clearer.

### 4.3 — A marker interface: `IAggregateRoot` (idiomatic, but NOT about `Create`)

The *one* entity-level abstraction that IS standard DDD is a **marker** — it identifies which
entities are aggregate roots (the persistence/transaction boundaries), so repositories can constrain
to them:

```csharp
public interface IAggregateRoot;   // no members — a capability tag

public class Category : AMasterEntity<CategoryId>, IAggregateRoot { … }

// enables: IRepository<T> where T : class, IAggregateRoot
```

It has **zero members** and nothing to do with construction. It's an *identity/role* marker, not a
factory abstraction. Add it when you build repositories; it's the correct entity-level interface —
the one you were reaching for, just serving a different purpose than `Create`.

---

## Part 5 — The rule, generalized (so you can decide alone next time)

When a new "can I abstract this?" itch strikes, ask **in this order**:

1. **Is the thing I'd hoist a MECHANIC (identical body) or POLICY (varying body)?**
   - Mechanic + identical → **abstract base**. (IDs, single-value VOs.)
   - Policy + varying → **do not inherit.** Go to 2.
2. **Is there cross-cutting PLUMBING inside the varying bodies?**
   - Yes → extract a **composition helper** you *use* (`ErrorCollector`, a guard clause, a spec).
   - No → **leave it duplicated.** Distinct rules that merely *look* similar are **not** duplication
     (DRY is about knowledge, not keystrokes — two rules that happen to both check length are two
     decisions, not one).
3. **Do I need to treat many types uniformly (repository, dispatcher)?**
   - Yes → a **marker interface** (`IAggregateRoot`) or a **capability interface** (`IAuditable`) —
     you already do this. That's role-typing, not logic-sharing.

> **The trap to name:** "these two blocks look similar, so they should share a parent." Similar
> *appearance* ≠ shared *reason to change*. Abstract on **shared reason to change** (that's SRP),
> never on visual resemblance. Entity factories resemble each other and share **no** reason to
> change — so they stay apart.

---

## Part 6 — SOLID scorecard for the "don't abstract entity Create" decision

| Principle | Why keeping factories local (and composing helpers) wins |
|-----------|----------------------------------------------------------|
| **SRP** | Each factory changes only when *its* aggregate's rules change. A shared base `Create` would change for *every* aggregate — the definition of a bad, magnet-for-edits class. |
| **OCP** | Adding `Product` requires **zero** edits to existing code. A base factory would need modification (new branch/param) per aggregate — modifying to extend. |
| **LSP** | There's no false "Product is-a GenericFactory" substitution to break, because we never asserted it. |
| **ISP** | `IAggregateRoot` (empty) and `IAuditable` (tiny) keep interfaces minimal; nobody depends on a fat `IEntityFactory` they can't satisfy. |
| **DIP** | Factories depend on abstractions they own (VOs, `Result`); no inversion is gained by a base factory. |
| **Composition > inheritance** | `ErrorCollector` is *had*, not *inherited* — reuse without hierarchy coupling. |
| **DRY (correctly scoped)** | We de-duplicate **plumbing** (ErrorCollector) and **mechanics** (ABaseEntity), never **rules**. |

---

## Part 7 — Cheat sheet & FAQ

```
"SHOULD I ABSTRACT THIS?" — 10-second decision
──────────────────────────────────────────────
Is the hoisted body IDENTICAL everywhere?
   ├─ YES → is it a MECHANIC (Value/ToString/equality/identity)? → abstract BASE CLASS  ✅
   └─ NO  → is it POLICY (rules/params/events)?
             ├─ shared PLUMBING inside?  → COMPOSITION helper (ErrorCollector)          ✅
             ├─ need uniform typing?     → MARKER interface (IAggregateRoot)            ✅
             └─ otherwise                → LEAVE IT LOCAL. It's not duplication.        ✅

ENTITY MECHANICS ALREADY CENTRALIZED (don't re-invent):
   ABaseEntity     → Id, equality, ==, GetHashCode, domain-event list, RaiseDomainEvent
   AMasterEntity   → CreatedAt/UpdatedBy/IsDeleted/IsActive (audit + soft-delete)
   ⇒ the entity's "AStronglyTypedId" is ABaseEntity. The factory is NOT part of it — by design.
```

| Temptation | Why it's wrong | Do instead |
|------------|----------------|------------|
| `abstract Result<T> Create(...)` base for entities | body is 100% policy; nothing to inherit; signatures don't even match | per-aggregate `static Create`; document the *shape* as a convention |
| `IEntityFactory<T>` interface with `Create` | static factories aren't polymorphic; args differ per entity → useless uniformity | nothing — or `ErrorCollector` for the plumbing |
| Sharing rules because two factories "look alike" | resemblance ≠ shared reason to change; couples unrelated aggregates | keep separate; extract only true cross-cutting plumbing |
| Putting event-raising in a base `Create` | which event differs per aggregate; timing is aggregate-specific | raise inside each aggregate's own factory (it owns its events) |
| A base class just to hold `new List<Error>()` | that's composition's job, not inheritance's | `ErrorCollector` local variable |

---

### TL;DR

- **IDs & single-value VOs** shared **mechanics** → abstract base was right. ✅
- **Entity factories** are **policy** → **no base class.** The shared mechanics you'd want are
  already in `ABaseEntity`/`AMasterEntity`; the rest is per-aggregate on purpose.
- Want reuse anyway? **Compose** (`ErrorCollector`) and **mark** (`IAggregateRoot`) — never inherit
  a factory.
- Decide future cases with one question: **mechanic-and-identical → inherit; policy-and-varying →
  compose or leave local.**
```
