# NovaWallet Ledger Service

A simplified wallet ledger for FirstBank NovaPay's NovaWallet module, built in C# / .NET 9.

## Example values used throughout this README (and while testing)

These are arbitrary, caller-chosen strings — not something the server generates or issues.
Reused consistently below and in the demo/smoke test so it's easy to follow along.

| Purpose | Example values |
|---|---|
| `customerId` | `cust-001`, `cust-002`, `cust-003` ... |
| `Idempotency-Key` header | `Idempotency-Key-001`, `Idempotency-Key-002`, `Idempotency-Key-003` ... |

A real client would typically generate the Idempotency-Key as a fresh UUID per request; the
`-001`, `-002` style here is just for readability while testing and demoing.

## Architecture

Four-layer solution, dependencies point inward:

```
NovaWallet.Api            → Controllers, JWT middleware, Problem Details, Swagger, rate limiting
NovaWallet.Application    → WalletService, TransferService (all business rules live here)
NovaWallet.Domain         → Wallet, Transaction, AuditLogEntry, IdempotencyKey, OutboxMessage (no EF Core dependency)
NovaWallet.Infrastructure → EF Core + SQL Server, DbContext, Repositories, Unit of Work, Outbox publisher
NovaWallet.Tests          → xUnit, including the concurrency load test
```

The Domain layer has zero dependency on EF Core or ASP.NET Core — entities are plain C# with
factory methods and invariants enforced in constructors/methods (e.g. `Wallet.Debit` throws
rather than allowing a negative balance to be constructed). Application depends only on Domain
and a repository + Unit of Work abstraction (`IWalletRepository`, `ITransactionRepository`,
`IAuditLogRepository`, `IIdempotencyKeyRepository`, `IOutboxRepository`, all aggregated behind
`IUnitOfWork`), which keeps the concurrency-critical logic testable and readable without EF Core
details bleeding into it. Application has **zero package dependency on EF Core** as a result —
only Infrastructure knows SQL Server or EF Core exist at all.

## Datastore: SQL Server (not Postgres)

The brief didn't specify a datastore. I chose **SQL Server** deliberately — it's a common choice
in the kind of enterprise/banking stack this scenario is modelled on, and its `UPDLOCK`/`ROWLOCK`
query hints give precise, well-understood control over pessimistic locking, which is the
approach I used for concurrency safety (see below).

## How money is represented

All amounts are `long` (kobo), everywhere — Domain entities, DTOs, database columns (`bigint`).
There is no `float`/`double`/`decimal` anywhere in the money path. ₦500,000 is a compile-time
constant `500_000_00` kobo (`TransferService.DailyLimitKobo`) so the unit is impossible to
misread at the call site.

`Wallet.Credit` uses `checked` arithmetic around the balance addition, so a credit that would
overflow `long.MaxValue` throws a clean `BalanceOverflowException` instead of silently wrapping
the balance around to a large negative number. At real Naira balances this is astronomically
unlikely to matter (`long.MaxValue` kobo is on the order of tens of quadrillions of Naira) - but a
ledger's correctness guarantees shouldn't quietly depend on "unlikely."

## Concurrency safety — the core of the exercise

**Approach: pessimistic row locking via `WITH (UPDLOCK, ROWLOCK)`.**

`IWalletRepository.GetForUpdateAsync` (implemented in `WalletRepository`, Infrastructure layer)
reads a wallet row with an explicit SQL Server lock hint:

```sql
SELECT * FROM Wallets WITH (UPDLOCK, ROWLOCK) WHERE Id = @id
```

`UPDLOCK` takes an update lock at read time (rather than a shared lock that gets upgraded later,
which is what causes classic deadlocks under contention). `ROWLOCK` asks the engine not to
escalate to a page/table lock. Combined with an explicit database transaction
(`IsolationLevel.ReadCommitted`), the effect is: once one request has locked a wallet row, any
other request trying to lock the *same* row blocks until the first transaction commits or rolls
back. Reads and writes against a wallet are fully serialized without needing `SERIALIZABLE`
isolation (which would be heavier-handed and hurt throughput more than necessary here).

**Deadlock avoidance:** a transfer locks two wallets (`from` and `to`). If two concurrent
transfers move money in opposite directions between the same pair of wallets (A→B and B→A
simultaneously), locking "from" first and "to" second would deadlock. `TransferService` avoids
this by always locking wallets in a fixed, deterministic order — ordinal string comparison of
the two wallet GUIDs — independent of transfer direction (`OrderIds` in `TransferService.cs`).

**Why not optimistic concurrency (a `RowVersion`/xmin column)?** It's a valid alternative and
I considered it. I chose pessimistic locking because a ledger's write path is contention-heavy
by nature (many transfers can legitimately target the same wallet in a short window), and
optimistic concurrency would mean retry loops under load rather than a single serialized path —
harder to reason about correctness of under the time constraints of this exercise, and harder to
write a crisp, convincing concurrency test for. Optimistic concurrency would likely scale better
under very high write throughput on a single hot wallet; that's the trade-off.

## Idempotency

`POST /transfers` requires an `Idempotency-Key` header. The key, a SHA-256 hash of the request
payload, and the serialized response are all written to an `IdempotencyKeys` table **inside the
same database transaction** as the transfer itself. The check-then-act sequence (has this key
been used? if not, process and record it) happens under the same wallet row locks as the
transfer, so two concurrent replays of the same key cannot both pass the "not yet used" check
before either commits — the second one blocks until the first transaction resolves, then sees
the now-committed key and returns the cached response instead of double-processing.

Reusing a key with a *different* payload (different amount, different wallets) is rejected with
`409 Conflict` rather than silently processed or silently returning a stale response.

**Update: the residual edge case below is now closed.** The original design serialized replays
by locking the two wallets *before* checking the idempotency key — correct for a genuine replay
(same key, same payload, therefore the same two wallets), since the second concurrent request
would contend on the same wallet locks and only reach the idempotency check after the first had
committed. It did **not** protect the narrower case of the same key reused with a *different*
payload targeting *different* wallets, submitted truly concurrently — those two requests didn't
contend on any shared lock, so both could pass the "not yet used" check before either committed,
and the loser would hit a raw database primary-key violation instead of a clean `409`.

That gap is now closed with `IIdempotencyKeyRepository.AcquireProcessingLockAsync` — a SQL Server
`sp_getapplock` advisory lock keyed on the idempotency key itself (via `TransferService`,
acquired as the very first step, before any wallet is touched). Because the lock is keyed on the
*key*, not on any wallet, **any** two concurrent requests carrying the same key now serialize here
regardless of which wallets their payloads reference — closing the gap the wallet-lock ordering
alone couldn't. The wallet-lock ordering is still what makes a genuine replay resolve to the
correct cached response rather than reprocessing; the two mechanisms now work together rather
than one covering for a hole in the other.

This is covered by two tests: `Transfer_SameKeySamePayload_ConcurrentReplays_ProcessedExactlyOnce`
(8 identical concurrent requests, same wallets — the case the wallet-lock ordering always handled)
and, at the database layer, `AuditLog_DatabaseRejectsUpdate` and
`FailedTransfer_DoesNotAppendAuditRows` in `DailyLimitAndAuditTests.cs` cover related invariants
introduced alongside this change.

## Repository pattern + Unit of Work

`Application` depends only on its own interfaces (`IWalletRepository`, `ITransactionRepository`,
`IAuditLogRepository`, `IIdempotencyKeyRepository`, `IOutboxRepository`, aggregated behind
`IUnitOfWork`) — no `DbSet<T>`, no `IDbContextTransaction`, no EF Core package reference at all.
`Infrastructure` provides the concrete EF Core-backed implementations
(`WalletRepository`, `TransactionRepository`, etc.) and a `UnitOfWork` that owns them and
coordinates the shared `NovaWalletDbContext`/transaction across all of them. `WalletService` and
`TransferService` take a single `IUnitOfWork` constructor dependency rather than five separate
repository parameters, since every write they do needs to participate in the *same* transaction —
that coordination is exactly what a Unit of Work is for. `IAuditLogRepository` is deliberately
write-only (`Add` is its only member) — there is no `Update`/`Delete` to even call, so the
append-only guarantee is enforced by the shape of the abstraction, not just by convention.

**Wallet creation race:** `CreateWalletAsync` checks "does this customer already have a wallet?"
and then inserts - not atomic on its own, so two concurrent requests for the same customer id
could both pass that check before either commits. This is closed the same way as the idempotency
gap above: `IWalletRepository.AcquireCustomerCreationLockAsync` takes a `sp_getapplock` advisory
lock keyed on a hash of the customer id, acquired *before* the existence check, inside an explicit
transaction. There's no wallet row to place a row-lock on yet (the customer doesn't have one) -
an advisory lock on a resource *name* rather than a row is exactly what `sp_getapplock` is for.
The second concurrent request now blocks until the first has committed or rolled back, so it only
ever sees an accurate "already exists" (with the real wallet's id, via `GetByCustomerIdAsync`) —
never a stale "not yet created."

`UnitOfWork.SaveChangesAsync` still catches the underlying unique-constraint violation on
`Wallets.CustomerId` and translates it into the same `CustomerAlreadyHasWalletException`, as a
defensive backstop — with the lock in place this path should never actually be exercised in
practice, but it means a duplicate insert would still surface as a clean `409` rather than a raw
`500` even if the locking were ever bypassed or misconfigured.

## Outbox pattern (`TransferCompleted` event)

An `OutboxMessages` table is written inside the **same database transaction** as the transfer
itself (`TransferService.TransferAsync`, right before `SaveChangesAsync`/`CommitAsync`) — so the
event can never be recorded unless the transfer actually committed, and can never be silently
lost if it did. A background `IHostedService` (`OutboxPublisherService`, in
`NovaWallet.Infrastructure`) polls for unpublished rows every 5 seconds, "publishes" them, and
marks them published. This exercise has no real message broker to publish to, so "publishing"
here means structured logging via `ILogger` — the poll/publish/mark-published mechanics are the
part worth demonstrating, and would carry over unchanged if a real broker (Azure Service Bus,
Kafka, etc.) were plugged in later.

## Statement

Paginated transaction history, newest first (`ORDER BY CreatedAt DESC`), with a secondary sort on
`Id` as a tiebreaker. `CreatedAt` alone isn't a safe sort key for pagination — two transactions
can legitimately share the same timestamp (`DateTimeOffset` precision, or just two writes landing
in the same tick), and without a tiebreaker, SQL Server is free to order ties differently between
separate page queries, which can cause a row to appear on two pages, or on neither, as data
changes underneath. `Id` is arbitrary but stable, which is all a tiebreaker needs to be.

## Daily limit (₦500,000/day, resets at WAT midnight)

Computed on the fly from the sum of `TransferOut` transactions for the wallet on the current WAT
calendar date (`DateTimeExtensions.ToWatDate`, a fixed UTC+1 offset — WAT does not observe
daylight saving, so this is safe as a constant offset rather than needing IANA timezone data).
This check happens under the same wallet lock as the transfer, so two concurrent transfers that
would each individually be under the limit, but together would breach it, cannot both succeed.
Tests cover both sides of the boundary: a transfer of exactly the limit succeeds
(`Transfer_AtExactlyTheDailyLimit_Succeeds`), and one kobo over fails
(`Transfer_ExceedingDailyLimit_Throws`) - not just the over-limit case in isolation.

## Audit log

`AuditLogEntries` is a separate table from `Transactions`. No application code path exposes an
update or delete against it — `AuditLogEntry` only has a static factory method and no mutators,
and `IAuditLogRepository` exposes only `Add()`.

This is also enforced at the **database level**, not just in application code: an
`AFTER UPDATE, DELETE` trigger on `AuditLogEntries` (`TR_AuditLogEntries_Immutable`) `THROW`s
unconditionally, rejecting any attempt to modify or remove a row regardless of which login issues
it. This lives in the `HardenLedgerInvariants` migration (applied by `Database.Migrate()` on
startup like any other migration) rather than as a separate ad-hoc startup step - migrations are
the right place for anything that changes what the schema guarantees, trigger bodies included, and
keeping it there means it shows up in migration history and gets applied exactly once, in order,
alongside everything else. A trigger is deliberately stronger than a `GRANT`/`DENY`-based approach
here: this exercise's `docker-compose.yml` connects as `sa`, which would simply ignore a `DENY`
grant, but cannot bypass a trigger without dropping it first.

The same migration adds five `CHECK` constraints (`Wallets.BalanceKobo >= 0`,
`Wallets.Currency = 'NGN'`, `Transactions.AmountKobo > 0`,
`AuditLogEntries.ResultingBalanceKobo >= 0`, `AuditLogEntries.DeltaKobo <> 0`) and two foreign
keys (`Transactions`/`AuditLogEntries` → `Wallets`, `ReferentialAction.Restrict`). None of these
duplicate application logic exactly - they're a second, independent layer that holds even if a
future code change somehow bypassed `Wallet.Debit`/`Credit`'s own checks. Defense in depth: the
application should never violate these, and now it provably can't even if it tried.

## Auth

A **mock JWT issuer** (`POST /auth/token`) signs tokens with a symmetric key from configuration,
exactly as the brief allows ("a simplified/mock issuer is fine — the point is the middleware and
claims handling, not building a full auth server"). All wallet/transfer endpoints require a valid
bearer token (`[Authorize]`); the `sub` claim carries the customer id.

`JwtBearerOptions.MapInboundClaims` is explicitly set to `false`. Without this, some JWT handler
configurations silently remap the `sub` claim to a legacy XML claim type
(`ClaimTypes.NameIdentifier`) for backwards compatibility - which would silently break every
literal `"sub"` claim lookup used for authorization and rate limiting below.

## Authorization: authentication alone is not enough

`[Authorize]` only proves a request carries a *valid* token - on its own it says nothing about
whether that caller should be allowed to touch the specific wallet in the URL. Every wallet and
transfer operation additionally checks that the caller's `sub` claim matches the wallet's
`CustomerId`:

- `GetBalance`, `Credit`, `GetStatement` — the wallet in the route must belong to the caller.
- `CreateWallet` — a caller may only create a wallet for their own customer id.
- `Transfer` — the caller must own the **sending** wallet (`fromWalletId`). The receiving wallet
  can belong to anyone, since that's the entire point of a P2P transfer.

A mismatch throws `ForbiddenException`, mapped to `403 Forbidden`. This check happens inside
`TransferService`/`WalletService` after the wallet is fetched (and, for transfers, after it's
locked) - it applies uniformly regardless of whether the request turns out to be new or a replay.

**Assumption worth flagging:** `CreditWalletAsync` also enforces this ownership check, even though
a real inbound NIP settlement credit would more realistically be triggered by a trusted internal
service, not the customer's own session. This API has no such second actor - only the
customer-facing JWT - so applying the same ownership rule here is the safer default for what this
endpoint can otherwise be used for today.

## Errors

All exceptions map to RFC 7807 Problem Details via a single `IExceptionHandler`
(`NovaWalletExceptionHandler`), so every error response — 400, 403, 404, 409, 422, 500 — has the
same shape (`type`, `title`, `status`, `detail`, `instance`, plus a `correlationId` extension).
Unrecognized exceptions are logged with full detail server-side but never leak internals to the
caller.

**401 and 429 are also Problem Details-shaped, and needed separate handling to get there.**
`IExceptionHandler` only intercepts genuine .NET exceptions thrown during request processing.
Two responses in this API never throw one: the JWT bearer handler writes a 401 directly when a
token is missing/invalid/expired, and the rate limiter writes a 429 directly when a caller exceeds
the limit - both by design, both without an exception ever occurring. Left alone, these would be
bare status codes with no body, breaking the "every error has the same shape" guarantee. Fixed via
`JwtBearerEvents.OnChallenge` and `RateLimiterOptions.OnRejected` in `Program.cs`, each writing the
same Problem Details shape by hand.

## Stretch goals implemented

- **Repository pattern + Unit of Work** — see above. `Application` has zero EF Core dependency.
- **Outbox pattern** for `TransferCompleted` events — see above.
- **Rate limiting** on `POST /transfers` (10 requests / 10s per authenticated subject - genuinely
  per-subject: see the "Authorization" section above for why the partition key reads the `sub`
  claim explicitly rather than `Identity.Name`).
- **Correlation IDs**: every request gets an `X-Correlation-Id` (reused if the caller already
  sent one), pushed into the logger scope, echoed back in the response, and included in every
  Problem Details response.
- **Health endpoints**: `GET /health/live` (process is up) and `GET /health/ready` (can reach the
  database), suitable for container orchestration liveness/readiness probes.

## Hardening fixes from a self/peer review

Before submitting, I ran a deliberately adversarial pass over my own implementation - the kind of
review I'd want a panelist to do - and fixed everything that held up under scrutiny:

- **Wallet ownership authorization** (see "Authorization" above) — `[Authorize]` alone proved you
  hold a valid token, not that you should be able to touch a given wallet. Every operation now
  checks the caller's `sub` claim against the wallet's `CustomerId`.
- **Rate limiter was silently IP-based, not per-subject** — the partition key read
  `Identity.Name`, which our JWT never populates, so it always fell back to IP. Fixed to read the
  `sub` claim directly.
- **401 and 429 weren't Problem Details-shaped** — both are written directly by middleware that
  never throws an exception, so `IExceptionHandler` never saw them. Fixed via `OnChallenge` and
  `OnRejected`.
- **Integer overflow on `Wallet.Credit`** — unchecked arithmetic could theoretically wrap a
  balance to a large negative number. Now `checked`, throwing `BalanceOverflowException`.
- **Concurrent duplicate wallet creation returned a raw 500** — originally caught reactively at
  the database level after the fact; now prevented at the root with a `sp_getapplock` advisory
  lock acquired before the existence check even runs (see "Repository pattern" above). The
  reactive translation in `UnitOfWork.SaveChangesAsync` remains as a defensive backstop.
- **Statement pagination had no tiebreaker** — `CreatedAt` alone isn't a stable sort key; added
  `Id` as a secondary sort.
- **Audit log immutability was only application-level** — closed with a database trigger, now
  living in a proper versioned migration (`HardenLedgerInvariants`) alongside five `CHECK`
  constraints and two foreign keys, rather than as an ad-hoc startup step. See "Audit log" above.
- **Idempotency's core guarantee was only tested sequentially, and had a real residual gap** —
  added a genuinely concurrent replay test firing 8 identical requests in parallel, and closed the
  gap itself: a second `sp_getapplock` advisory lock, keyed on the idempotency key rather than any
  wallet, now serializes *any* two concurrent requests sharing a key regardless of which wallets
  their payloads reference. See "Idempotency" above.
- **Cross-test flakiness under the shared SQL Server connection pool** — xUnit's default
  parallel-test-class execution, combined with the concurrency tests already opening dozens of
  simultaneous connections, produced intermittent connection-pool-exhaustion failures. Fixed with
  `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
- **Daily-limit rollover was never actually tested** — added `DailyLimit_ResetsAtMidnightWAT`,
  which advances a test clock across the WAT day boundary and proves the limit genuinely resets,
  rather than only testing the same-day over/at-limit cases.

## Stretch goals *not* implemented (and why)

- **Structured logging via Serilog**: built-in `ILogger` + the correlation ID middleware covers
  the "traceable across a request" requirement; a dedicated sink (Seq, Application Insights)
  would be the next step for real observability but wasn't essential to demonstrate here.

## How to run

Requires Docker and Docker Compose.

```bash
docker compose up
```

That's genuinely it — both migrations (`InitialCreate` and `HardenLedgerInvariants`) are already
generated and committed in `src/NovaWallet.Infrastructure/Migrations/`, so `Database.Migrate()`
applies them automatically on startup. No `dotnet ef migrations add` step is needed for a fresh
clone; that command is only for when you change the schema further and need a *new* migration.

Swagger UI is available at `http://localhost:8080/swagger` once the API is up.

**Quick smoke test:**

```bash
curl -X POST http://localhost:8080/auth/token -H "Content-Type: application/json" \
  -d '{"customerId":"cust-001"}'
# copy the accessToken from the response into TOKEN below

curl -X POST http://localhost:8080/wallets -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"customerId":"cust-001"}'
# copy the walletId from the response — this is FROM_WALLET_ID below

curl -X POST http://localhost:8080/wallets -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"customerId":"cust-002"}'
# copy this response's walletId too — this is TO_WALLET_ID below

curl -X POST http://localhost:8080/wallets/$FROM_WALLET_ID/credit -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"amountKobo":100000,"narration":"opening balance"}'

curl -X POST http://localhost:8080/transfers -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -H "Idempotency-Key: Idempotency-Key-001" \
  -d "{\"fromWalletId\":\"$FROM_WALLET_ID\",\"toWalletId\":\"$TO_WALLET_ID\",\"amountKobo\":5000,\"narration\":\"test transfer\"}"

# Replay the exact same request with the SAME Idempotency-Key — should return
# the identical cached response, not process the transfer twice:
curl -X POST http://localhost:8080/transfers -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -H "Idempotency-Key: Idempotency-Key-001" \
  -d "{\"fromWalletId\":\"$FROM_WALLET_ID\",\"toWalletId\":\"$TO_WALLET_ID\",\"amountKobo\":5000,\"narration\":\"test transfer\"}"

# Reuse the same key with a DIFFERENT payload — should return 409 Conflict:
curl -X POST http://localhost:8080/transfers -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -H "Idempotency-Key: Idempotency-Key-001" \
  -d "{\"fromWalletId\":\"$FROM_WALLET_ID\",\"toWalletId\":\"$TO_WALLET_ID\",\"amountKobo\":9999,\"narration\":\"different payload\"}"
```

## Running the tests

The concurrency and idempotency tests run against a real SQL Server instance (an in-memory EF
Core provider can't faithfully emulate row-level locking or `sp_getapplock`, which are exactly
the things under test).

```bash
docker compose up -d db
dotnet ef database update --project src/NovaWallet.Infrastructure --startup-project src/NovaWallet.Api
dotnet test
```

Test classes run sequentially, not in parallel (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`
in `TestAssembly.cs`). All of these tests share one real SQL Server instance and its connection
pool; letting xUnit's default parallelization run multiple test classes at once — on top of the
concurrency tests already opening dozens of simultaneous connections by themselves — is what
caused intermittent connection-pool-exhaustion failures in practice. Sequential test classes cost
a little wall-clock time but make the suite reliably reproducible.

## Assumptions made (documented per the brief's instruction)

- One wallet per customer id (enforced by a unique index on `CustomerId`); the brief doesn't say
  whether multiple wallets per customer should be possible, and a single wallet is the simpler,
  more common case for this kind of e-wallet product.
- The daily limit applies to outbound transfers only (`TransferOut`), not inbound credits — the
  brief's phrasing ("daily outbound transfer limit") supports this reading.
- "Statement" pagination defaults to 20 items per page, capped at 100, consistent with typical
  API pagination defaults.
- A caller can only act on wallets they own (matched by the JWT's `sub` claim against
  `Wallet.CustomerId`), including for `CreditWallet` — see the "Authorization" section above for
  why this was applied there too despite the brief modeling credits as inbound settlements.
