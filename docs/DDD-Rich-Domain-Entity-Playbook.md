# Rich Domain Model & Factory Pattern — The BookCart Playbook

> A repeatable, deeply-explained recipe for turning an **anemic** entity (a bag of public
> setters) into a **rich** DDD aggregate that can never exist in an invalid state.
> Tailored to _this_ codebase: `AMasterEntity<TId>`, `Result` / `Error`, `AStronglyTypedId`.
>
> Read Part 1 once to build the mental model. After that, **Part 2 is your fast checklist** —
> glance at it whenever you build a new aggregate. Part 3 is the deep "why / what breaks"
> reference you dip into when you forget _why_ a step exists.

---

## Part 0 — The two words that explain everything

|                           | Anemic model (what `Category` is now)                             | Rich model (where we're going)                                 |
| ------------------------- | ----------------------------------------------------------------- | -------------------------------------------------------------- |
| **State**                 | `public string Name { get; set; }`                                | `public Name Name { get; private set; }`                       |
| **Who guards the rules?** | The _caller_ (a service). Rules are scattered and easy to forget. | The **entity itself**. Rules live in one place, always run.    |
| **Can it be invalid?**    | Yes — `new Category()` gives you an empty, nameless row.          | **No** — the only door in (`Category.Create`) validates first. |
| **Construction**          | `new` keyword, public.                                            | `private` constructor + `static Create(...)` factory.          |

The single sentence to memorise:

> **Make illegal states unrepresentable. => اجعل الدول غير القانونية غير قابلة للتمثيل.**
> If a `Category` with an empty name is illegal,
> there must be _no sequence of public calls_ that produces one.

Everything below is mechanics in service of that one sentence.

---

## Part 1 — The mental model: an aggregate has 6 collaborators

A rich entity is never alone. When you "DDD-ify" `Category`, you are really building a small
**cast of files** around it. Know the cast before you write step 1:

```
Entities/Categories/
├─ Category.cs                    ← 1. THE AGGREGATE ROOT   (this playbook's main subject)
├─ CategoryErrors.cs              ← 2. ERROR CATALOG        (already exists ✅)
├─ ValueObjects/
│   ├─ CategoryId.cs              ← 3. IDENTITY VO          (already exists ✅)
│   └─ CategoryName.cs            ← 4. PROPERTY VOs         (wrap every primitive)
├─ Events/
│   └─ CategoryCreatedDomainEvent.cs  ← 5. DOMAIN EVENTS    (facts that happened)
└─ Enums/
    └─ (e.g. CategoryStatus.cs)   ← 6. ENUMS               (closed sets of states)
```

**Dependency direction (never violate this):**
`Category` → depends on → `CategoryId`, `CategoryName`, `CategoryErrors`, events, `Result`.
None of those ever depend back on `Category`. Errors and VOs are _leaves_; the aggregate is
the _trunk_. (This is why `AStronglyTypedId` reports a `FormatCheck` reason instead of
referencing `CategoryErrors` — the base must stay a leaf.)

---

## Part 2 — THE FAST CHECKLIST (your muscle memory)

When you sit down to make _any_ aggregate rich, do these in order. Each maps to a deep-dive in Part 3.

| #   | Step                     | One-line action                                                                                        | Deep-dive   |
| --- | ------------------------ | ------------------------------------------------------------------------------------------------------ | ----------- |
| 1   | **Pick base + identity** | `class Category : AMasterEntity<CategoryId>`                                                           | [§3.1](#31) |
| 2   | **EF constructor**       | Add `private Category() : base() { }`                                                                  | [§3.2](#32) |
| 3   | **Factory constructor**  | Add `private Category(CategoryId id, …) : base(id) { … }`                                              | [§3.3](#33) |
| 4   | **Lock the state**       | Every property → `{ get; private set; }`; wrap primitives in VOs; `= null!` on required refs           | [§3.4](#34) |
| 5   | **The `Create` factory** | `public static Result<Category> Create(…)` → validate, collect errors, build, raise event              | [§3.5](#35) |
| 6   | **Domain methods**       | One named method per state transition, each returns `Result`, mutates via private setter, raises event | [§3.6](#36) |
| 7   | **Supporting cast**      | Fill in the VO, the event, extend the error catalog                                                    | [§3.7](#37) |

> **The 30-second version:** _private parameterless ctor (EF) → private full ctor (factory) →
> private setters + VOs → `static Result<T> Create` that validates then raises an event →
> named `Result`-returning methods for every change._

---

## Part 3 — DEEP DIVE (the "why", and the exact error if you skip it)

<a id="31"></a>

### Step 1 — Pick the base class and the strongly-typed identity

```csharp
public class Category : AMasterEntity<CategoryId>   // was: AMasterEntity<Guid>
```

**What this buys you.** `AMasterEntity<TId>` already gives `Category` its identity, equality,
domain-event list, and audit/soft-delete columns (`CreatedAt`, `IsDeleted`, …). You inherit all
of it — your job is only the _business_ shape.

**Why `CategoryId` instead of `Guid`?**

- **Type safety.** `void Move(CategoryId id, BookId book)` — you _cannot_ accidentally swap the
  two arguments; the compiler rejects it. With two `Guid`s, swapping compiles and ships a bug.
- **Self-describing.** IDs read `category-019750ab-…` in logs and URLs.
- **Sortable.** UUIDv7 embeds a timestamp → rows sort by creation time with no extra column.

> **Sequencing tip:** build the `CategoryId` VO _before_ the entity (you already did). The entity's
> generic parameter needs the type to exist. This is why the identity VO is Step 0 in real life.

**⚠️ Persistence footnote (later, not now):** because `Id` is now a custom type, EF Core will need
a **value converter** (`CategoryId ↔ string`) when you configure the DbContext. No converter →
runtime error _"The property 'Category.Id' could not be mapped … no type mapping for 'CategoryId'"_.
That belongs in the Infrastructure layer, not here — the domain stays persistence-ignorant.

---

<a id="32"></a>

### Step 2 — The private _parameterless_ constructor (for EF Core)

```csharp
//! For EF Core materialisation ONLY. Never call from domain or application code.
private Category() : base() { }
```

This is the step everyone forgets and then can't explain. Master it.

**Why does EF need its own constructor at all?**
When EF reads a row from the database it must _rehydrate_ an object. It does **not** go through
your `Create` factory (that would re-run validation and re-raise "Created" events for a category
that already exists — nonsense). Instead EF instantiates the object and pours the column values
into the backing fields via reflection.

**Why `private` and not `public`?**
Visibility is your enforcement tool. `public` would let application code write `new Category()`
and get a nameless, invalid object — exactly the illegal state we outlawed. `private` means **only
EF's reflection** (which ignores accessibility) and the class's own factory can reach it. The
domain door stays shut; the ORM's service entrance stays open.

**What is the _exact_ error if you skip it?**
If your entity has _only_ the [[parameterised factory constructor]] and no [[parameterless]] one that EF
can bind, EF throws at model-building / first query:

> `InvalidOperationException: No suitable constructor was found for entity type 'Category'.
The following constructors had parameters that could not be bound to properties of the entity type: …`

**Why [[parameterless constructor]] specifically?** EF _can_ bind a parameterised constructor by matching parameter
names to properties — but with Value Objects (`Name`, not `string`) the names/types don't line up
cleanly, so the reliable, intention-revealing choice is: **one parameterless ctor for EF, one rich
ctor for the factory.**

> **Fast rule:** _Every rich entity gets `private Ctor() : base() { }` as line one of its body.
> Write it first, before you write anything else. It costs nothing and prevents the #1 EF error._

---

<a id="33"></a>

### Step 3 — The private _full_ constructor (for the factory)

```csharp
private Category(CategoryId id, CategoryName name /*, … */) : base(id)
{
    Name = name;
    // …assign every property. NOTHING ELSE. No validation. No events.
}
```

**Why `private`?** Same reason as Step 2 — the _only_ legitimate caller is `Create`. If it were
`public`, a caller could hand in an unvalidated `CategoryName`… except they can't, because VOs are
_also_ only creatable through _their_ factories. This is defence in depth: the entity guards
entity-rules, each VO guards its own field-rules.

**Why `protected` on the base (`ABaseEntity`) but `private` here?**
Look at `ABaseEntity`: its constructors are `protected`. `protected` = "usable by **derived
classes**". `Category` is a derived class, so it can call `base(id)`. But `Category` doesn't want
_its own_ constructor exposed even to future subclasses — a `Category` isn't meant to be subclassed
into invalid variants — so it uses the stricter `private`.
**The rule:** _base ctors are `protected` (so children can chain to them); leaf-entity ctors are
`private` (so only the factory builds them)._

**Why must this constructor be "dumb" (pure assignment, no logic)?**
A constructor's contract is _"I have finished building a valid object."_ Validation and event-raising
are **side-effects that can fail or fire** — they don't belong in a ctor, because a half-thrown
constructor leaves a ghost object, and raising a "Created" event before the object is fully assigned
is a latent bug. So: **the factory decides & validates; the constructor only assigns.**

---

<a id="34"></a>

### Step 4 — Lock down state: `private set`, Value Objects, `null!`

```csharp
public CategoryName Name { get; private set; } = null!;   // required reference VO
public CategoryStatus Status { get; private set; }         // enum (value type, no null!)
public DateTime? ArchivedAt { get; private set; }          // genuinely optional
```

**Why `private set` and not `set`?**
`public set` is the definition of an anemic model — anyone can bypass your rules with
`category.Name = whatever`. `private set` means _"the value can change, but only through code
inside this class"_ — i.e. through a domain method that first checks the invariants. State still
mutates (a category _can_ be renamed); it just can't mutate _unguarded_.

**`private set` vs `init` vs no setter — when each?**

- `init` → set once at construction, immutable forever. Great for things that never change.
- `private set` → mutable, but only from inside. Use for anything a domain method updates (`Name`,
  `Status`). **This is your default for rich entities**, because domain methods need to write.
- read-only (`{ get; }`) → set only in the constructor via a field. Rare for entities; common in VOs.

**Why wrap `string Name` in a `CategoryName` VO instead of leaving it a `string`?**
A raw `string` can be `""`, 5000 chars, or `"   "`. Every place that receives it must re-validate,
and someone will forget. A `CategoryName` **cannot be constructed invalid** (its own `Create`
trims & length-checks), so once you hold one, it's _provably_ valid. Primitives never escape the
domain boundary — this is the "Primitive Obsession" cure.

**Why `= null!` on `CategoryName Name`?**
`Name` is non-nullable but _isn't_ assigned in the parameterless EF ctor (EF fills it right after,
via the backing field). Without an initializer the compiler warns:

> `CS8618: Non-nullable property 'Name' must contain a non-null value when exiting constructor.`

`= null!` is the **null-forgiving** operator: _"I promise this is assigned before anyone reads it —
stop warning me."_ You use it because **you** (via factory or EF) guarantee assignment. Value-type
props (`enum`, `bool`, `DateTime`) and nullable refs (`DateTime?`) don't need it — only _required
reference types_.

---

<a id="35"></a>

### Step 5 — The `Create` factory (the heart of it)

```csharp
public static Result<Category> Create(string name /*, other primitives */)
{
    var errors = new List<Error>();

    // 1. Validate each field THROUGH ITS VALUE OBJECT, collecting (not throwing) errors.
    Result<CategoryName> nameResult = CategoryName.Create(name);
    if (nameResult.IsFailure)
        errors.AddRange(nameResult.Errors);

    // 2. Validate entity-level / cross-field rules here (rules a single VO can't know).

    // 3. If ANY rule failed, return the WHOLE list — the caller sees every problem at once.
    if (errors.Count > 0)
        return errors;                       // implicit: List<Error> → Result<Category> (failure)

    // 4. Only now — everything valid — mint identity and build via the private ctor.
    var category = new Category(CategoryId.New(), nameResult.Value);

    // 5. Record the fact that a Category was born.
    category.RaiseDomainEvent(new CategoryCreatedDomainEvent(category.Id!));

    return category;                          // implicit: Category → Result<Category> (success)
}
```

**Why a static factory instead of a public constructor?** Four concrete wins:

1. **It can fail gracefully.** A constructor can only _throw_; `Create` returns `Result<Category>`
   — failure becomes a value you handle, not an exception you catch.
2. **It can run side-effects a ctor shouldn't** — chiefly **raising a domain event**. "A category
   was created" is a _fact about the domain_, and the factory is the natural place to announce it.
3. **It hides construction details.** Callers say _what_ they want (`Create("Fiction")`), not _how_
   it's assembled (ID generation, VO wiring). You can change the internals freely.
4. **Named intent.** `Create`, and later `Reconstitute`, `Import`, … read better than overloaded
   `new`.

**Why collect errors in a `List<Error>` instead of returning on the first failure?**
UX. A user submitting a form wants _all_ the mistakes at once ("name too long **and** status
invalid"), not one, fix, resubmit, next one. Collect every broken rule, then return the batch.
(`return errors;` works because of the `implicit operator Result<T>(List<Error>)` in your `Result`.)

**Why `category.Id!` (the `!`) when raising the event?**
`ABaseEntity` declares `TId? Id` — nullable, because a brand-new-not-yet-persisted entity is
"transient". But _inside_ `Create`, right after `new Category(CategoryId.New(), …)`, you **know**
`Id` is set. `!` tells the compiler "I've reasoned about this; it's non-null here."

**What breaks if you put this logic in a constructor instead?** You lose the `Result` (ctors can't
return one, so you'd throw), and you either raise the event from a ctor (fires before the object is
fully valid / testable) or forget it entirely. The factory exists precisely to hold what a ctor
can't.

---

<a id="36"></a>

### Step 6 — Domain methods: one named method per state transition

Every change to a rich entity after birth goes through a **verb**. The shape is always the same:

```csharp
public Result Rename(string newName)
{
    // (a) guard: is this transition even allowed right now?
    if (IsDeleted)
        return CategoryErrors.CannotModifyDeleted;

    // (b) validate the input through its VO
    Result<CategoryName> nameResult = CategoryName.Create(newName);
    if (nameResult.IsFailure)
        return nameResult.Errors.ToList();

    // (c) mutate — legal because we're INSIDE the class (private set)
    Name = nameResult.Value;

    // (d) announce the fact (optional, if anyone cares that it changed)
    RaiseDomainEvent(new CategoryRenamedDomainEvent(Id!));

    // (e) report success
    return Result.Success();
}
```

**Why methods instead of public setters?** A setter answers _"can this field change?"_. A method
answers the real question: _"under what conditions, with what validation, and with what
consequences (events) can this change?"_ `Rename` can refuse when deleted; a setter can't refuse
anything. This is the whole point of encapsulation — behaviour travels **with** the data.

**Why does each method return `Result` (not `void`, not `bool`)?**
Because a transition can be _rejected by a business rule_ ("can't rename a deleted category"), and
the caller must be told _why_. `Result` carries either success or the specific `Error`. `void`
would silently swallow the rejection; `bool` would hide the reason.

**The invariant that makes this airtight:** combine Step 4 (`private set`) with Step 6 (methods) and
there is **literally no way** for outside code to change a `Category` without going through a guard.
That's "illegal states unrepresentable," enforced by the compiler.

---

<a id="37"></a>

### Step 7 — The supporting cast (build these alongside)

**A property Value Object** — same pattern as `CategoryId`, but validating _content_ not _format_:

```csharp
public sealed record CategoryName
{
    public const int MaxLength = 100;
    public string Value { get; }
    private CategoryName(string value) => Value = value;   // private → only Create builds it

    public static Result<CategoryName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CategoryErrors.Name.Empty;

        value = value.Trim();
        if (value.Length > MaxLength)
            return CategoryErrors.Name.TooLong;

        return new CategoryName(value);
    }

    public override string ToString() => Value;
}
```

_Same three pillars as the identity VO: `private` ctor, `static Create` returning `Result`, invalid
state impossible. A `record` gives you value-equality for free (two names with the same text are
equal)._

**A domain event** — a past-tense fact, immutable:

```csharp
public sealed record CategoryCreatedDomainEvent(CategoryId CategoryId) : IDomainEvent;
```

_Past tense (`Created`, not `Create`) because it describes something that **already happened**.
Infrastructure (an EF interceptor + MediatR) publishes these after save. Named in the past because
you can't un-raise a fact._

**Extend the error catalog** — group by concern, like the existing `CategoryErrors.Id`:

```csharp
public static class CategoryErrors
{
    public static class Id { /* … already there … */ }

    public static class Name
    {
        public static readonly Error Empty   = Error.Validation("Category.Name.Empty",   "Category name is required.");
        public static readonly Error TooLong = Error.Validation("Category.Name.TooLong", $"Category name must be ≤ {CategoryName.MaxLength} characters.");
    }

    public static readonly Error CannotModifyDeleted = Error.Conflict("Category.Deleted", "A deleted category cannot be modified.");
}
```

---

## Part 4 — `Category`: before → after (the whole payoff on one screen)

**BEFORE — anemic (today):**

```csharp
public class Category : AMasterEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
}
// A service must remember to validate Name, set the Id, raise events… every time. It won't.
```

**AFTER — rich (the target shape):**

```csharp
public class Category : AMasterEntity<CategoryId>
{
    // ── ctors ──────────────────────────────────────────────
    private Category(CategoryId id, CategoryName name) : base(id) => Name = name;
    private Category() : base() { }                       // EF only

    // ── state (locked) ─────────────────────────────────────
    public CategoryName Name { get; private set; } = null!;

    // ── factory ────────────────────────────────────────────
    public static Result<Category> Create(string name)
    {
        Result<CategoryName> nameResult = CategoryName.Create(name);
        if (nameResult.IsFailure)
            return nameResult.Errors.ToList();

        var category = new Category(CategoryId.New(), nameResult.Value);
        category.RaiseDomainEvent(new CategoryCreatedDomainEvent(category.Id!));
        return category;
    }

    // ── behaviour ──────────────────────────────────────────
    public Result Rename(string newName)
    {
        Result<CategoryName> nameResult = CategoryName.Create(newName);
        if (nameResult.IsFailure)
            return nameResult.Errors.ToList();

        Name = nameResult.Value;
        return Result.Success();
    }
}
```

`new Category()` is now impossible from outside. The _only_ way to get one is `Category.Create(...)`,
which _cannot_ return an invalid category. The rules moved from "hopefully every service remembers"
to "the compiler guarantees."

---

## Part 5 — One-page cheat sheet (pin this)

```
RICH AGGREGATE — build order
────────────────────────────
0. VOs first        CategoryId, CategoryName      → private ctor + static Result Create()
1. Base + Id        : AMasterEntity<CategoryId>
2. EF ctor          private Category() : base() {}          ← prevents "No suitable constructor"
3. Factory ctor     private Category(id, …) : base(id) {…}  ← pure assignment, no logic
4. Lock state       { get; private set; }  + VOs  + = null! on required refs
5. Create()         static Result<T>: validate → collect errors → build → RaiseDomainEvent
6. Methods          verb per transition: guard → validate → mutate → event → Result.Success()
7. Cast             Events (past-tense record) + grow CategoryErrors

VISIBILITY DECODER
──────────────────
private   ctor/setter  → only this class (factory + EF reflection)   ← entity leaf ctors, all setters
protected ctor         → this class + subclasses                     ← base (ABaseEntity) ctors
public    static Create→ the ONLY construction door for callers
= null!               → "required ref, assigned by factory/EF, stop CS8618"
Id!                   → "transient Id is set by now, trust me"
return Result<T>      → transition may be refused; caller learns why

WHY, IN 5 LINES
───────────────
private ctor        → block `new`, force the validating factory
parameterless ctor  → EF rehydration path (skips validation/events, correctly)
private set         → state changes only through guarded methods
Value Objects       → primitives can't escape invalid; validate once, trust forever
Result not throw    → failures are values you handle, collected in a batch
```

---

### FAQ — the "what if I don't?" table

| You skip…                                     | Symptom / exact error                                                                                    |
| --------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `private Category() : base(){}`               | `InvalidOperationException: No suitable constructor was found for entity type 'Category'.` at query time |
| making ctors `private`                        | anemic model returns — callers `new` up invalid entities, bypassing all rules                            |
| `= null!` on required ref VO                  | `CS8618: Non-nullable property 'Name' must contain a non-null value…` warning                            |
| wrapping primitives in VOs                    | validation duplicated everywhere; someone forgets; bad data reaches the DB                               |
| returning `Result` from methods               | rejections vanish (`void`) or lose their reason (`bool`) — caller can't react                            |
| raising events in `Create` (doing it in ctor) | events fire before the object is provably valid; ctor throws leave ghost half-objects                    |
| the EF value converter (Infra, later)         | `The property 'Category.Id' could not be mapped … no type mapping for 'CategoryId'`                      |

```

```
