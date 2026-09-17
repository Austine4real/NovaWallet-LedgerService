# AI Usage

I used Claude (Anthropic) throughout this project — for scaffolding the solution, talking through
design decisions, writing tests, and reviewing my own code. Here's an honest account of how I
used it, and a few times it got things wrong that I had to catch myself.

## What I used it for

- Setting up the initial project structure (Domain, Application, Infrastructure, Api) and the
  first draft of the entities and services.
- Talking through the concurrency approach before I picked one — I went back and forth on
  pessimistic locking vs. optimistic concurrency with it before deciding.
- Writing the first draft of the idempotency logic, the daily limit check, and the error handling.
- Writing the first version of the concurrency test.
- Reviewing this README and this file for anything I'd missed.

## A few prompts I actually used

**"Scaffold a .NET solution for a wallet ledger service with Domain, Application, Infrastructure,
and Api projects. Use SQL Server, UPDLOCK/ROWLOCK for concurrency-safe transfers, idempotency
keys, a daily limit that resets at WAT midnight, and an append-only audit log."**
What came back: a working project skeleton with the locking pattern already wired up through raw
SQL locking reads inside a transaction, plus the entities and the error-handling middleware. Good
starting point — but it's also where Mistake #1 below came from, so I didn't just accept it as-is.

**"Write a test that proves the transfer endpoint can't be double-spent under concurrent load."**
What came back: a test that fires many concurrent transfers from a wallet that can only afford
some of them, and checks the exact number that succeed and the final balance. I added one more
check myself — that the *receiving* wallet's balance also matches exactly — because the original
version could have missed a bug where money got copied instead of moved.

**"Why did you use `long` in kobo instead of `decimal` for money? Most C# advice says decimal is
the right type."**
What came back: a genuinely good explanation of why `decimal` isn't as safe as it sounds for a
ledger — see Mistake #3 below for the full reasoning.

## Mistake #1: it got the order of operations wrong on the money path

This is the one I take most seriously. The first draft of the transfer logic checked whether an
Idempotency-Key had already been used **before** locking the two wallets involved. On paper that
looks fine, and it even passes a simple test where you only send one request at a time.

But here's the problem: if the exact same request comes in twice at the same moment (which is
the whole point of idempotency — a client retrying after a timeout), both requests could check
"has this key been used?" and both get told "no," because neither has actually saved anything
yet. Then both would try to process the transfer, and whichever one finished second would crash
with a database error instead of just quietly returning the same result as the first one.

I only found this by asking myself (and Claude) to walk through, step by step, what actually
happens in the database if two identical requests land at the exact same time — not just
re-reading the code and assuming it looked right. Once I traced it through, the fix was simple:
lock the wallets **first**, then check the idempotency key. Since a real retry always targets the
same two wallets, the second request now has to wait for the first one to finish before it even
gets to the idempotency check — so it just sees "already done" instead of crashing.

**What I took from this:** code can look correct and even pass a basic test while still being
wrong under real concurrency. For anything touching money, I made a habit of actually tracing
through what happens when two requests hit at the same time, instead of trusting that the code
"looks like it should work."

## Mistake #2: a small but real code conflict

While setting up one of the interfaces, the first draft added a method that was meant to satisfy
an interface requirement — but it turned out the base class (`DbContext`) already had a method
with the exact same name and signature. So there were now two things trying to do the same job,
which is at best pointless and at worst confuses the compiler.

I caught this by checking what `DbContext` already provides on its own, rather than assuming the
generated code was filling a real gap. The fix was just deleting the extra method.

**What I took from this:** it's easy for AI-written code to look reasonable on its own but clash
with something already there that it didn't fully account for. Worth double-checking, especially
around anything to do with saving data.

## Mistake #3: the "just use decimal" advice

I asked why I'd used a plain whole number (`long`, counted in kobo) for money instead of the
`decimal` type most C# tutorials recommend. The honest answer is: "use decimal for money, never
float" is common advice, and it's not wrong exactly — it's just not the whole story.

`decimal` avoids the rounding problems `float`/`double` have, but it's still not a perfect fit for
a ledger. It doesn't force a fixed number of decimal places, and — this is the part that surprised
me — when a `decimal` amount leaves the API as JSON, most other languages (like JavaScript) will
read it back as the exact kind of imprecise number we were trying to avoid in the first place.

Kobo sidesteps all of that. It's the smallest unit of the currency, so there's nothing smaller to
round — a whole number is always exactly right, in every language, every time it's sent over the
network.

**What I took from this:** the most commonly repeated advice isn't always the deepest answer.
"Use decimal for money" is good general advice, but a ledger has sharper requirements than most
apps, and it was worth pushing past the first answer to understand why.
