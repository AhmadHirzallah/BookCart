# The `params` Keyword — A Complete, Practical Guide (C# / .NET 10)

> **Goal:** after this you can read, write, and *reason about* any `params` method — including
> the modern `params ReadOnlySpan<T>` you already use in `Result.Combine`. Includes when to use it,
> the traps, performance, and graded exercises **with solutions**.

---

## Part 1 — The one-sentence idea

> **`params` lets a method accept a *variable number of arguments* as if they were a single
> collection parameter — the caller can pass zero, one, or many values separated by commas, and
> the compiler bundles them into the collection for you.**

```csharp
int Sum(params int[] numbers)          // declaration
{
    int total = 0;
    foreach (int n in numbers) total += n;
    return total;
}

Sum();            // → 0   (empty)
Sum(5);           // → 5   (one)
Sum(1, 2, 3, 4);  // → 10  (many)   ← this is the magic: no array literal needed
Sum(new[] { 1, 2, 3 });               // → 6  (you can STILL pass a real array)
```

Without `params`, that last-but-one line would have to be `Sum(new[] { 1, 2, 3, 4 })`. `params` is
**syntactic sugar for the caller** — it removes the `new[] { … }` ceremony.

---

## Part 2 — The rules (memorize these five)

1. **Last parameter only.** `params` must be the **final** parameter. `void M(params int[] xs, int y)`
   is illegal — how would the compiler know where the params end?
2. **At most one** `params` per method.
3. **The caller has two forms:**
   - **Expanded form:** `Sum(1, 2, 3)` — pass loose values.
   - **Normal form:** `Sum(myArray)` — pass the collection directly.
4. **Zero arguments is legal** → you get an **empty** collection (not `null`). `Sum()` → `int[0]`.
5. **The parameter is a normal collection inside the method** — iterate it, index it, `.Length` it.

```csharp
void Log(string category, params object[] args)   // ✅ params is last
{
    Console.WriteLine($"[{category}] {args.Length} arg(s)");
}
Log("db");                       // 0 args
Log("db", "connect", 42, true);  // 3 args
```

---

## Part 3 — Classic `params T[]` vs modern `params` collections (C# 13+, .NET 9/10)

For 20 years `params` meant **`params T[]`** only. Since **C# 13** it works with many collection
types — most importantly the allocation-free **`params ReadOnlySpan<T>`** and **`params Span<T>`**:

```csharp
void A(params int[] xs)              { }          // classic — heap array every call
void B(params ReadOnlySpan<int> xs)  { }          // modern — usually NO heap allocation
void C(params List<int> xs)          { }          // params over List<T>
void D(params IEnumerable<int> xs)   { }          // params over any enumerable
```

**Why the span form matters (the reason your `Combine` uses it):**

```csharp
// YOUR CODE — BookCart.Domain/Common/Result/Result.cs
public static Result Combine(params ReadOnlySpan<Result> results) { … }

Result.Combine(nameResult, descriptionResult);   // caller looks identical to params T[]
```

- **`params int[]`**: every `Sum(1,2,3)` call **allocates a `int[3]` on the heap** → GC pressure in
  hot paths.
- **`params ReadOnlySpan<int>`**: the compiler stores the loose arguments in a **stack-allocated
  buffer** and hands the method a `ReadOnlySpan` view of it → **zero heap allocation**. Same call
  syntax, better performance. (Deep-dive on spans in the companion guide.)

> **Rule of thumb:** for a **new** method that only *reads* its arguments, prefer
> **`params ReadOnlySpan<T>`**. Keep `params T[]` when callers legitimately pass real arrays they
> already hold, or when you must target older frameworks.

---

## Part 4 — What the compiler actually does (so nothing surprises you)

`Sum(1, 2, 3)` is rewritten by the compiler to (roughly):

```csharp
// params int[]  →  allocates:
Sum(new int[] { 1, 2, 3 });

// params ReadOnlySpan<int>  →  stack buffer, no heap:
Span<int> __tmp = stackalloc int[3];
__tmp[0] = 1; __tmp[1] = 2; __tmp[2] = 3;
Sum(__tmp);
```

**Consequences you must know:**
- **Overload resolution prefers the non-`params` (normal) form when it fits.** If you have both
  `void M(int a, int b)` and `void M(params int[] a)`, then `M(1, 2)` calls the **first** — the
  exact match wins; the expanded form is a fallback.
- **A method with more overloads can pick "normal form" over "expanded form"** — if you have
  `void M(params int[] xs)` and call `M(myIntArray)`, it uses **normal form** (passes the array
  directly, no wrapping). `M(1, 2)` uses **expanded form** (wraps).

---

## Part 5 — The traps (every one bites a real developer)

### 5.1 — Passing a single `null`

```csharp
void P(params object[] args) => Console.WriteLine(args?.Length.ToString() ?? "NULL array");

P(null);          // → "NULL array"   ⚠️ the WHOLE args array is null, not {null}
P((object)null);  // → "1"            a one-element array holding null
P(null, null);    // → "2"
```
`P(null)` binds `null` to the array parameter itself (normal form) — you get a **null array**, and
`args.Length` throws `NullReferenceException`. **Defend with `args is { Length: > 0 }` or check
`null`,** or accept a span (a span is never null — it's `default`/empty instead).

### 5.2 — `params object[]` swallows an array you meant to spread

```csharp
void Print(params object[] items) => Console.WriteLine(items.Length);

string[] names = { "a", "b", "c" };
Print(names);            // → 3   (string[] IS object[] → normal form, spread as 3 items)
Print((object)names);    // → 1   (one item: the array itself)
int[] nums = { 1, 2, 3 };
Print(nums);             // → 1   ⚠️ int[] is NOT object[] → treated as ONE object!
```
`int[]` is not covariantly an `object[]`, so it becomes a **single** element. `string[]` *is*
`object[]`, so it spreads. This asymmetry is a classic bug in logging/format APIs.

### 5.3 — Allocation in hot loops

```csharp
for (int i = 0; i < 1_000_000; i++)
    total += Sum(i, i + 1, i + 2);   // params int[] → 1,000,000 heap arrays 😱
```
Use `params ReadOnlySpan<int>` (zero alloc) or a non-params overload for the hot arity.

### 5.4 — Ambiguity when you add a `params ReadOnlySpan<T>` beside `params T[]`

If a type has **both** `M(params int[])` and `M(params ReadOnlySpan<int>)`, C# 13 resolves
`M(1,2,3)` to the **span** overload (it's "better"). Passing an actual `int[]` variable picks the
array overload. Usually fine — but don't declare both unless you have a reason.

---

## Part 6 — Real-world `params` you already use every day

| API | Signature (essence) | Why params |
|-----|---------------------|------------|
| `Console.WriteLine` / `string.Format` | `Format(string fmt, params object?[] args)` | arbitrary number of substitutions |
| `string.Concat` | `Concat(params string?[] values)` | join N strings |
| `Path.Combine` | `Combine(params string[] paths)` | join N path segments |
| `string.Join` | `Join(string sep, params object?[] values)` | glue N values |
| `Array` / LINQ helpers, `Math`-style `Max` | often `params` | N inputs |
| **Your `Result.Combine`** | `Combine(params ReadOnlySpan<Result> results)` | validate N results in one guard |

---

## Part 7 — When to use `params` (decision guide)

**✅ Use it when:**
- The count is genuinely variable and small-to-moderate (`Sum`, `Combine`, `Log`, `Max`).
- The call site reads better as loose args than as `new[] { … }`.
- You're building a *convenience/aggregation* API.

**❌ Avoid / reconsider when:**
- The count is fixed (2 or 3 always) → just declare the parameters; it's clearer and faster.
- You're in a **hot path** and stuck on `params T[]` → provide **non-params overloads** for the
  common arities (this is exactly what the BCL does: `string.Concat` has `(string,string)`,
  `(string,string,string)`, *and* `(params string[])`).
- Callers almost always already have a real collection → take `IEnumerable<T>`/`IReadOnlyList<T>`
  directly; `params` buys nothing.

**⚡ Performance ladder (fastest → most flexible):**
```
fixed params (int a, int b)  >  params ReadOnlySpan<T>  >  params T[]  >  params IEnumerable<T>
```

---

## Part 8 — EXERCISES (do them, then check the solutions)

> Try each yourself first. Solutions follow in §9.

**E1 (warm-up).** Write `int Max(params int[] values)` that returns the largest, and returns
`int.MinValue` for an empty call. Then call it three ways.

**E2 (spread trap).** Predict the output *without running it*:
```csharp
void Show(params object[] xs) => Console.WriteLine(xs.Length);
string[] s = { "x", "y" };
int[]    n = { 1, 2, 3 };
Show(s);
Show(n);
Show((object)s);
Show();
```

**E3 (allocation-free).** Rewrite `Max` from E1 as `params ReadOnlySpan<int>` so a million calls in
a loop allocate nothing. What must change in the body (hint: empty check)?

**E4 (BookCart real).** Using your `Result.Combine`, write a `Book.Create` factory sketch that
validates **four** value objects (`BookTitle`, `Isbn`, `Price`, `StockQuantity`) with a **single**
guard, then constructs. (Pseudocode VOs are fine.)

**E5 (overload resolution).** Given:
```csharp
void M(int a, int b)          => Console.WriteLine("pair");
void M(params int[] xs)       => Console.WriteLine("params");
```
What prints for `M(1, 2)`, `M(1)`, `M(1, 2, 3)`, `M()`? Explain each.

**E6 (design).** You're writing a `Logger.Info(string message, params object[] args)` for structured
logging, but it's called in tight loops. Give **two** designs that keep the ergonomic call site while
avoiding a heap array on every call.

---

## Part 9 — SOLUTIONS

**S1.**
```csharp
int Max(params int[] values)
{
    if (values.Length == 0) return int.MinValue;
    int max = values[0];
    for (int i = 1; i < values.Length; i++)
        if (values[i] > max) max = values[i];
    return max;
}
Max();                 // int.MinValue
Max(7);                // 7
Max(3, 9, 2, 9, 1);    // 9
Max(new[] { 4, 4 });   // 4  (normal form)
```

**S2.**
```
Show(s);         → 2   string[] is object[] → spread as 2 items
Show(n);         → 1   int[] is NOT object[] → ONE object (the array)
Show((object)s); → 1   explicitly one object (the array reference)
Show();          → 0   empty array, length 0 (never null here)
```

**S3.** Spans can't be `null` and don't need `new[]`; the empty check uses `IsEmpty` (or `Length==0`):
```csharp
int Max(params ReadOnlySpan<int> values)
{
    if (values.IsEmpty) return int.MinValue;
    int max = values[0];
    for (int i = 1; i < values.Length; i++)
        if (values[i] > max) max = values[i];
    return max;
}
// for (…) Max(i, i+1, i+2);  → zero heap allocations; the temp buffer is on the stack.
```

**S4.**
```csharp
public static Result<Book> Create(string title, string isbn, decimal price, int stock)
{
    Result<BookTitle>     titleR = BookTitle.Create(title);
    Result<Isbn>          isbnR  = Isbn.Create(isbn);
    Result<Price>         priceR = Price.Create(price);
    Result<StockQuantity> stockR = StockQuantity.Create(stock);

    var validation = Result.Combine(titleR, isbnR, priceR, stockR);   // ← ONE guard, four VOs
    if (validation.IsFailure)
        return validation.Errors;

    var book = new Book(BookId.New(), titleR.Value, isbnR.Value, priceR.Value, stockR.Value);
    // book.RaiseDomainEvent(new BookCreatedDomainEvent(book.Id!));
    return book;
}
```

**S5.**
```
M(1, 2)     → "pair"    exact non-params match beats expanded params form
M(1)        → "params"  no (int) overload exists → expanded params with one element
M(1, 2, 3)  → "params"  no (int,int,int) overload → expanded params
M()         → "params"  empty expanded form (empty array); no ()-less non-params overload
```
**Lesson:** the compiler prefers the most specific *applicable* non-params overload; `params` is the
catch-all fallback.

**S6.** Two ergonomic + fast designs:
1. **Non-params overloads for common arities** (BCL pattern):
   ```csharp
   void Info(string msg) { … }
   void Info(string msg, object a) { … }
   void Info(string msg, object a, object b) { … }
   void Info(string msg, params object[] args) { … }   // fallback for 3+
   ```
2. **`params ReadOnlySpan<object?>`** (C# 13) — one method, zero allocation for the loose-arg case:
   ```csharp
   void Info(string msg, params ReadOnlySpan<object?> args) { … }
   ```
   (Modern logging libraries also use *source generators* + interpolated-string handlers to skip
   boxing entirely — an advanced third option.)

---

## Part 10 — Cheat sheet

```
DECLARE
  void M(params T[] xs)              classic; heap array per expanded call
  void M(params ReadOnlySpan<T> xs)  C#13+; stack buffer, no heap; read-only args   ← prefer for new read-only APIs
  void M(params List<T> xs)          C#13+; when you need a growable list inside
  params is ALWAYS the LAST parameter; AT MOST ONE per method

CALL
  M(a, b, c)     expanded form  → compiler bundles a,b,c
  M(collection)  normal form    → passes it directly (T[] / matching type)
  M()            empty          → empty collection (NOT null for T[]; default/empty for span)

OVERLOAD RESOLUTION
  exact non-params overload  >  expanded params form   (specific beats catch-all)

TRAPS
  M(null)                → null ARRAY (NRE risk); spans avoid this (never null)
  M(int[])  vs  object[] → int[] becomes ONE object; string[] spreads (covariance)
  hot loops + params T[] → allocations; use ReadOnlySpan or fixed overloads

PERF LADDER (fast → flexible)
  (a, b)  >  params ReadOnlySpan<T>  >  params T[]  >  params IEnumerable<T>
```

### TL;DR
`params` = "pass 0..N loose args, I'll bundle them." Old sugar was `params T[]` (allocates); modern
C# adds **`params ReadOnlySpan<T>`** (allocation-free) — which is exactly why your `Result.Combine`
is declared that way. Prefer fixed parameters when the count is fixed, span-params for new read-only
variadic APIs, and array-params only when callers hand you real arrays or you target old runtimes.
```
