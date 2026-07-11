# `Span<T>` & `ReadOnlySpan<T>` — A Complete, Practical Guide (C# / .NET 10)

> **Goal:** truly *understand* what a span is (not just copy snippets), know exactly how
> `Span<T>` and `ReadOnlySpan<T>` differ, feel *when a span is the right tool*, and prove it with
> real problems you solve with spans — including graded exercises **with solutions**.

---

## Part 1 — The mental model: a span is a *window*, not a container

Forget the name for a second. A span is **two fields**: a *reference to where some memory starts*
and a *length*. That's it.

```
        ┌─────────────────────────────────────────────┐
array:  │ 10 │ 20 │ 30 │ 40 │ 50 │ 60 │ 70 │ 80 │ 90 │   (lives on the heap)
        └─────────────────────────────────────────────┘
                    ▲              ▲
                    │              │
   Span<int> window = array.AsSpan(2, 3);   // { ref → index 2 , length = 3 }
                    └──── views 30,40,50 ───┘
```

- A span **does not own or copy** the data. It's a **typed window** onto memory that already exists
  somewhere else (an array, the stack, a string, native memory).
- `window[0]` is `30` — the span *re-bases* indices to its start.
- Slicing is **O(1) and zero-copy**: `window.Slice(1)` just makes a new `{ref+1, len-1}` — no bytes move.

> **One sentence:** **`Span<T>` is a lightweight, bounds-checked, zero-copy view over a contiguous
> block of memory — regardless of where that memory lives.**

This is why spans are fast: operations that used to **copy** (like `string.Substring`) become
**re-pointing** (like `ReadOnlySpan<char>.Slice`).

---

## Part 2 — Why it exists: the problem it kills

**Before spans**, "give me part of this data" meant **allocating a copy**:

```csharp
string csv = "1990-01-15";
string year  = csv.Substring(0, 4);   // NEW string "1990"  (heap allocation)
string month = csv.Substring(5, 2);   // NEW string "01"    (heap allocation)
string day   = csv.Substring(8, 2);   // NEW string "15"    (heap allocation)
int y = int.Parse(year);              // …then parse
```
Three throwaway strings just to read three numbers. In a loop over a million rows that's **3 million
allocations** → GC storms.

**With spans**, no copies at all:

```csharp
ReadOnlySpan<char> csv = "1990-01-15";
int y = int.Parse(csv.Slice(0, 4));   // parse a WINDOW — no substring allocated
int m = int.Parse(csv.Slice(5, 2));
int d = int.Parse(csv.Slice(8, 2));
```
`int.Parse(ReadOnlySpan<char>)` reads straight from the window. **Zero** intermediate strings.

> **The span value proposition:** *slice, parse, search, and transform contiguous data without
> allocating copies.* That's the whole reason it was added (.NET Core 2.1 / C# 7.2).

---

## Part 3 — Where the memory can live (the "regardless of where" superpower)

The same `Span<T>`/`ReadOnlySpan<T>` type views **all** of these — one API, three memory kinds:

```csharp
// 1) Heap array
int[] arr = { 1, 2, 3, 4 };
Span<int> a = arr;                       // implicit; a is a window over the heap array

// 2) The STACK (no heap at all) — great for small, short-lived buffers
Span<int> b = stackalloc int[4];         // 4 ints carved out of the current stack frame
b[0] = 99;

// 3) A string's characters (READ-ONLY, because strings are immutable)
ReadOnlySpan<char> c = "hello".AsSpan(); // window over the string's chars; no copy
```

`stackalloc` + `Span<T>` is the classic combo for a **temporary buffer with no GC cost**. Because
the span carries a length and bounds-checks every index, `stackalloc` is now *safe* to use (the old
raw-pointer `stackalloc` was `unsafe`).

---

## Part 4 — `Span<T>` vs `ReadOnlySpan<T>`: the exact difference

They are the **same window idea**; the only difference is **whether you can write through it**.

| | `Span<T>` | `ReadOnlySpan<T>` |
|---|-----------|-------------------|
| Read elements (`x = s[i]`) | ✅ | ✅ |
| **Write** elements (`s[i] = x`) | ✅ **mutable window** | ❌ compile error |
| Made from a `string` | ❌ (strings are immutable) | ✅ (`"hi".AsSpan()`) |
| Made from `stackalloc`, `T[]` | ✅ | ✅ |
| Implicit `Span<T>` → `ReadOnlySpan<T>` | — | ✅ (widening is free & one-way) |
| Use as a **read-only parameter** | works, but over-permissive | ✅ **preferred** — signals "I won't mutate" |

```csharp
Span<int> mutable = stackalloc int[3];
mutable[0] = 10;                         // ✅ allowed

ReadOnlySpan<int> view = mutable;        // ✅ implicit widen to read-only
// view[0] = 5;                          // ❌ CS: cannot assign — it's read-only

ReadOnlySpan<char> chars = "book".AsSpan();
// Span<char> bad = chars;               // ❌ can't go read-only → mutable (would break immutability)
```

**The rule that decides which to use:**
- **Parameter that only READS** → take **`ReadOnlySpan<T>`**. It accepts *both* mutable and read-only
  callers and *documents* that you won't change their data. (This is why `int.Parse`, `string`
  APIs, and your `Result.Combine` take `ReadOnlySpan`.)
- **You need to WRITE into the buffer** (fill, sort in place, transform) → take **`Span<T>`**.
- **Returning/holding a view of a `string`** → it can *only* be `ReadOnlySpan<char>`.

> **Mnemonic:** *`ReadOnlySpan` is the "input" span; `Span` is the "workspace" span.* Default to
> `ReadOnlySpan<T>` and widen to `Span<T>` only when you must mutate.

---

## Part 5 — The catch: a span is a `ref struct` (stack-only) — and why

Spans are **`ref struct`s**, which means the type is **restricted to the stack**. This buys the
safety guarantee (a span can never outlive the buffer it points at) but imposes rules:

**A `ref struct` (so a `Span<T>`) ❌ CANNOT:**
- Be a **field of a class** or a normal (non-`ref`) struct.
- Be **boxed** (no `object o = span;`, no putting it in a `List<object>`).
- Be a **generic type argument** in most cases (`List<Span<int>>` ❌).
- Be captured by a **lambda / local function closure**.
- (Historically) live **across an `await` or `yield`** — modern C# relaxes some cases, but treat it
  as: *don't hold a span across an async boundary.* Use **`Memory<T>` / `ReadOnlyMemory<T>`** there.

**Why these rules?** The span points at memory (often a stack frame or an array). If you could stash
it on the heap (a field, a boxed object, a closure) it might **outlive** that memory → a dangling
pointer. The `ref struct` restriction makes that **impossible at compile time**. The awkwardness is
the safety.

```csharp
class Cache
{
    // Span<int> _buf;          // ❌ CS8345: a ref struct field is not allowed in a class
    Memory<int> _buf;           // ✅ Memory<T> is heap-safe; get a span from it when needed
}

Memory<int> mem = new int[100];
Span<int> work = mem.Span;      // borrow a span briefly, use it, let it go
```

> **`Span<T>` for synchronous, local, hot work. `Memory<T>` for anything stored, async, or
> long-lived** — then call `.Span` to get a working window at the moment you use it.

---

## Part 6 — The everyday span toolbox (methods you'll actually use)

```csharp
ReadOnlySpan<char> s = "  Book: Clean Code  ".AsSpan();

s.Trim();                       // ReadOnlySpan window with surrounding spaces removed (no new string!)
s.Slice(2);                     // from index 2 to end
s.Slice(2, 4);                  // 4 elements starting at index 2
s.IndexOf(':');                 // position of ':' or -1
s.StartsWith("Book".AsSpan());  // prefix test, zero-copy
s.Contains('C');                // membership
s.SequenceEqual(other);         // element-wise equality (spans don't use == for contents)

Span<int> nums = stackalloc int[5];
nums.Fill(7);                   // set all to 7
nums.Clear();                   // set all to default(T)
arr.AsSpan().Sort();            // in-place sort through a Span
data.CopyTo(dest);              // copy into another span (bounds-checked)
```

Handy factories: `array.AsSpan()`, `"text".AsSpan()`, `stackalloc T[n]`, `CollectionsMarshal.AsSpan(list)`
(view a `List<T>`'s backing array), `MemoryMarshal.*` (advanced reinterpretation).

⚠️ **`==` on spans compares the *window* (ref+length), NOT the contents.** For content equality use
**`SequenceEqual`**. For char spans, comparisons use `MemoryExtensions` overloads (`Equals(other,
StringComparison)`).

---

## Part 7 — WORKED EXAMPLES: problems where span is *the* right fit

### 7.1 — Split without allocating substrings

**Problem:** count how many CSV fields exceed 10 chars, over millions of lines, without allocating a
string per field.

```csharp
int CountLongFields(ReadOnlySpan<char> line)
{
    int count = 0;
    foreach (Range r in line.Split(','))   // .NET 9+: Span-based Split yields Ranges, no strings
    {
        if (line[r].Length > 10) count++;
    }
    return count;
}
// CountLongFields("alpha,verylongvalue,x".AsSpan()); → 1
```
*Why span:* each field is a **window** `line[r]`, never a `Substring`. Zero allocations per field.

### 7.2 — Parse a fixed-layout date without `Substring`

```csharp
DateOnly ParseYmd(ReadOnlySpan<char> s)   // "2026-07-11"
{
    int y = int.Parse(s.Slice(0, 4));
    int m = int.Parse(s.Slice(5, 2));
    int d = int.Parse(s.Slice(8, 2));
    return new DateOnly(y, m, d);
}
```
*Why span:* three `int.Parse(ReadOnlySpan<char>)` calls read windows directly — the old version
allocated three throwaway strings.

### 7.3 — Build a small string with a stack buffer (no `StringBuilder`)

```csharp
string MaskCardTail(ReadOnlySpan<char> digits)   // "…" + last 4, e.g. "1234" → "••1234" style
{
    Span<char> buf = stackalloc char[6];
    buf[0] = '•'; buf[1] = '•';
    digits.Slice(digits.Length - 4).CopyTo(buf.Slice(2));
    return new string(buf);           // single allocation: the final string only
}
```
*Why span:* the scratch buffer is on the **stack**; only the final `string` allocates.

### 7.4 — In-place normalize (mutation → `Span<T>`)

```csharp
void ToAsciiUpperInPlace(Span<char> text)     // needs WRITE → Span<char>, not ReadOnlySpan
{
    for (int i = 0; i < text.Length; i++)
        if (text[i] is >= 'a' and <= 'z')
            text[i] = (char)(text[i] - 32);
}
char[] title = "clean code".ToCharArray();
ToAsciiUpperInPlace(title);                   // title is now "CLEAN CODE" — no new array
```
*Why `Span` (not `ReadOnlySpan`):* we **write** through the window.

### 7.5 — BookCart-flavored: validate an ISBN-13 shape allocation-free

```csharp
bool LooksLikeIsbn13(ReadOnlySpan<char> raw)
{
    ReadOnlySpan<char> s = raw.Trim();        // no trimmed-string allocation
    if (s.Length != 13) return false;
    foreach (char c in s)
        if (c is < '0' or > '9') return false;
    return true;
}
// Perfect inside Isbn.Create(string value) → LooksLikeIsbn13(value.AsSpan())
```
*Why span:* a value-object validator runs on **every** create; keeping it allocation-free matters.

---

## Part 8 — EXERCISES (solve with spans, then check §9)

> For each: *why is a span the fit?* Then implement it allocation-aware.

**X1 (warm-up).** `int SumSlice(ReadOnlySpan<int> data, int start, int count)` — sum a sub-range
**without** creating a new array.

**X2 (string, zero-copy).** `bool IsPalindrome(ReadOnlySpan<char> s)` ignoring case, **no**
`Substring`, **no** `Reverse()` allocation.

**X3 (stackalloc).** `string ToHex(byte b)` returning two hex chars (e.g. `255` → `"ff"`) using a
`stackalloc char[2]` buffer and exactly **one** final allocation.

**X4 (mutation).** `void ReverseInPlace(Span<int> data)` — reverse the elements through the span,
no extra array. Why must the parameter be `Span<int>` and not `ReadOnlySpan<int>`?

**X5 (parse, real).** `bool TryParsePriceTag(ReadOnlySpan<char> tag, out decimal price)` for inputs
like `"$19.99"` — strip a leading `'$'` with `Slice`, parse the rest, no substrings.

**X6 (split, .NET 9+).** `int WordCount(ReadOnlySpan<char> text)` counting whitespace-separated words
using span splitting/scanning, allocating nothing.

**X7 (design/why).** Explain in one sentence each: (a) why you *can't* store a `Span<int>` in a
`class` field, (b) why `int.Parse` takes `ReadOnlySpan<char>` rather than `Span<char>`, (c) why
`"hi".AsSpan()` gives `ReadOnlySpan<char>` and never `Span<char>`.

**X8 (trap).** Predict: does `"abc".AsSpan() == "abc".AsSpan()` compare contents? What should you
call instead, and why?

---

## Part 9 — SOLUTIONS

**X1.** *Fit:* slicing is zero-copy.
```csharp
int SumSlice(ReadOnlySpan<int> data, int start, int count)
{
    int sum = 0;
    foreach (int n in data.Slice(start, count)) sum += n;
    return sum;
}
```

**X2.** *Fit:* two-pointer over a window, no reversal buffer.
```csharp
bool IsPalindrome(ReadOnlySpan<char> s)
{
    int i = 0, j = s.Length - 1;
    while (i < j)
    {
        if (char.ToLowerInvariant(s[i]) != char.ToLowerInvariant(s[j])) return false;
        i++; j--;
    }
    return true;
}
```

**X3.** *Fit:* stack scratch buffer; only the returned string allocates.
```csharp
string ToHex(byte b)
{
    ReadOnlySpan<char> digits = "0123456789abcdef";
    Span<char> buf = stackalloc char[2];
    buf[0] = digits[b >> 4];
    buf[1] = digits[b & 0xF];
    return new string(buf);
}
```

**X4.** *Fit:* in-place edit needs a **writable** window → `Span<int>`. `ReadOnlySpan` forbids
`data[i] = …`, so it can't reverse in place.
```csharp
void ReverseInPlace(Span<int> data)
{
    int i = 0, j = data.Length - 1;
    while (i < j) { (data[i], data[j]) = (data[j], data[i]); i++; j--; }
}
```

**X5.**
```csharp
bool TryParsePriceTag(ReadOnlySpan<char> tag, out decimal price)
{
    ReadOnlySpan<char> s = tag.Trim();
    if (s.StartsWith("$")) s = s.Slice(1);      // drop '$' — still a window, no substring
    return decimal.TryParse(s, out price);
}
```

**X6.** *Fit:* scan windows, count transitions.
```csharp
int WordCount(ReadOnlySpan<char> text)
{
    int count = 0;
    bool inWord = false;
    foreach (char c in text)
    {
        bool ws = char.IsWhiteSpace(c);
        if (!ws && !inWord) { count++; inWord = true; }
        else if (ws) inWord = false;
    }
    return count;
}
// (.NET 9+ alt: foreach (Range r in text.Split(' ')) { if (!text[r].IsEmpty) count++; })
```

**X7.**
- (a) A class lives on the **heap** and can outlive the stack frame / array the span points at;
  allowing the field would permit a **dangling** view → the `ref struct` rule bans it.
- (b) `int.Parse` only **reads** the characters, so `ReadOnlySpan<char>` is the honest, most
  permissive input — it also accepts spans over immutable **strings**.
- (c) A `string` is **immutable**; handing out a `Span<char>` (writable) would let callers mutate a
  string's contents, so only the read-only view is allowed.

**X8.** No — `==` on spans compares the **window identity** (pointer + length), not the characters.
Use **`s1.SequenceEqual(s2)`** (or `s1.Equals(s2, StringComparison.Ordinal)`), because content
equality must walk the elements; `==` deliberately doesn't, to keep it O(1).

---

## Part 10 — When to reach for a span (and when NOT to)

**✅ Use spans when:**
- You **slice/parse/search** contiguous data (strings, arrays, buffers) and want **no copies**.
- You need a **small, short-lived scratch buffer** → `stackalloc` + `Span<T>`.
- You're writing a **hot path / library** where allocations show up in profiles.
- You want an API that reads from arrays, strings, *and* stack buffers with **one** signature
  (`ReadOnlySpan<T>` parameter).

**❌ Don't bother / can't use spans when:**
- The data is **not contiguous** (a `LinkedList<T>`, a tree, `IEnumerable<T>` from LINQ).
- You must **store** the view (class field), cross an **`await`**, put it in a **collection**, or
  capture it in a **lambda** → use **`Memory<T>`/`ReadOnlyMemory<T>`** and take `.Span` at point of use.
- The code isn't allocation-sensitive and plain `Substring`/arrays are **clearer** — readability first.
- `stackalloc` size is **large or unbounded** → risk of stack overflow; cap it (e.g. ≤ ~256–1024
  elements) and fall back to a rented/pooled array (`ArrayPool<T>`) for big buffers.

---

## Part 11 — Cheat sheet

```
WHAT IT IS
  span = { reference to start , length }  → a zero-copy, bounds-checked WINDOW over contiguous memory
  Slicing is O(1): no bytes move, just a new {ref+offset, len}.

TWO FLAVORS
  ReadOnlySpan<T>  → read-only "input" window   (default for parameters; only kind for strings)
  Span<T>          → read/write  "workspace"    (use when you MUST mutate the buffer)
  Span<T> → ReadOnlySpan<T> : implicit, one-way.   string → ReadOnlySpan<char> only.

MEMORY SOURCES
  T[] array            → arr.AsSpan()
  stack               → Span<T> s = stackalloc T[n];   (small, no GC)
  string (chars)      → "text".AsSpan()  → ReadOnlySpan<char>
  List<T> backing     → CollectionsMarshal.AsSpan(list)

ref struct RULES (why span is stack-only)
  ❌ class/struct field   ❌ boxing   ❌ most generics   ❌ lambda capture   ❌ across await
  ➜ need any of those? use Memory<T>/ReadOnlyMemory<T>, then .Span at use site.

GOTCHAS
  ==  compares the WINDOW, not contents  → use SequenceEqual / Equals(…, StringComparison)
  stackalloc BIG          → stack overflow → cap size / ArrayPool<T> for large buffers
  hold a span too long    → compile error (that's the safety working)

PERF WIN
  Substring/Split copies  →  Slice/Split windows  =  fewer/zero allocations in hot paths
```

### TL;DR
A **span is a window** over memory you already have — slice/parse/search it with **no copies**.
**`ReadOnlySpan<T>` = read-only input** (and the only view a `string` gives); **`Span<T>` = writable
workspace**. It's a **`ref struct`**, so it's stack-only — great for fast, local, synchronous work;
for anything stored or async, use **`Memory<T>`** and grab `.Span` when you actually touch the bytes.
Your `params ReadOnlySpan<Result>` in `Combine` is this exact idea: the caller's loose arguments sit
in a **stack buffer**, and `Combine` reads them through a **read-only window** — zero heap allocation.
```
