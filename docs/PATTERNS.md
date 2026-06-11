# CartShop patterns

A catalog of the patterns this repo demonstrates, each linked to the slice
or class that puts it to work. Read this top to bottom for a guided tour, or
jump to a row when you want to crib the shape.

---

## Architecture

### Vertical slice
One file per command or query, holding request/response DTOs and a static
endpoint class with a Wolverine.Http attribute. No controllers, no DI
registration, no central route table.

→ [`Feature/Cart/Commands/CreateCart/Handler.cs`](../CartShop.Core/Feature/Cart/Commands/CreateCart/Handler.cs)
→ [`Feature/Cart/Queries/GetCart/Handler.cs`](../CartShop.Core/Feature/Cart/Queries/GetCart/Handler.cs)

### Aggregate with decision methods
State + `Apply(event)` methods + `Decide(...)` methods that return events.
Handlers load the aggregate, call a decision method, append the returned
event(s), and save. Invariants live on the aggregate, not in the handler.

→ [`Domain/CartAggregate.cs`](../CartShop.Core/Domain/CartAggregate.cs) — `Create`, `AddLine`, `RemoveLine`, `Submit`
→ [`Domain/InventoryView.cs`](../CartShop.Core/Domain/InventoryView.cs) — `EnsureCanReserve`

### Domain exceptions mapped to HTTP
Typed exceptions (`CartAlreadySubmitted`, `ItemNotInCart`, `InsufficientStock`)
are caught in handlers and translated to the appropriate status code.
Specific subtypes are caught before their base so the mapping is decided by
exception type, not message inspection.

→ [`Feature/Cart/Commands/SubmitCart/Handler.cs`](../CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs) — 409 vs 400
→ [`Feature/Cart/Commands/AddItem/Handler.cs`](../CartShop.Core/Feature/Cart/Commands/AddItem/Handler.cs) — full ladder

---

## Consistency boundaries

### Stream-per-aggregate (classic)
One stream = one entity. Identity is the stream id. Invariants that fit
inside one stream are enforced by loading the aggregate, deciding, appending.

→ [`Feature/Cart/Commands/SubmitCart/Handler.cs`](../CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs) — `AggregateStreamAsync<CartAggregate>` + `cart.Submit()`

### DCB (Dynamic Consistency Boundary)
The boundary is a **tag query**, not a stream id. Events tagged with the same
value are folded into an ephemeral view at decision time. `SaveChangesAsync`
throws `DcbConcurrencyException` if any matching event lands between read
and write, enforcing a cross-stream invariant atomically without merging
aggregates.

Two different shapes of invariant in the repo, both using the same primitive:

→ **Numeric balance** — [`Domain/InventoryView.cs`](../CartShop.Core/Domain/InventoryView.cs) + [`Feature/Cart/Commands/AddItem/Handler.cs`](../CartShop.Core/Feature/Cart/Commands/AddItem/Handler.cs) (reserved ≤ stock across every cart)
→ **One-shot claim** — [`Domain/CouponUsageView.cs`](../CartShop.Core/Domain/CouponUsageView.cs) + [`Feature/Cart/Commands/ApplyCoupon/Handler.cs`](../CartShop.Core/Feature/Cart/Commands/ApplyCoupon/Handler.cs) (coupon code applied at most once)
→ [`Initialization.cs`](../CartShop.Core/Initialization.cs) — `RegisterTagType<Sku>().ForAggregate<InventoryView>()`, `RegisterTagType<CouponCode>().ForAggregate<CouponUsageView>()`

> **Marten 9 note.** A DCB view (`InventoryView`, `CouponUsageView`) is a plain
> self-aggregating class used only through `FetchForWritingByTags<T>`. Because
> it's never registered as a projection, Marten 9's compile-time source
> generator can't see it — so identity-less views are marked
> `[BoundaryAggregate]` (`JasperFx.Events.Aggregation`) and the tag is coupled
> to the view via `.ForAggregate<T>()`. Without both, the DCB read throws
> `InvalidProjectionException` at request time. See
> [`UPGRADE-marten9-wolverine6.md`](UPGRADE-marten9-wolverine6.md).

#### Without DCB — what would this look like?

A teaching tour of why DCB exists. Same problem (oversell, or duplicate
coupon claim), four classical responses — all unsatisfying:

| Approach | What goes wrong |
|---|---|
| **One big "Inventory" aggregate** holding stock + every cart's reservations | Every cart write locks the same row. Throughput collapses; one slow cart blocks the next. |
| **Eventual consistency + compensation** (decouple reservation from stock; reconcile later) | Two carts can both pass their independent stock checks and oversell. Refund-after-the-fact is messy and lossy. |
| **Service-layer / cross-aggregate transaction** wrapping both aggregates in one DB transaction | Breaks the "one aggregate per transaction" rule the rest of the codebase relies on. Doesn't scale to more than two participants. |
| **Process manager / saga** orchestrating reserve → confirm with compensations | Complex; the rule isn't visible in any one place; still has a reservation window where you can oversell. |

DCB collapses all four into one primitive: *"the events matching this query
**are** my transaction boundary right now."* The four-step shape is always
the same:

```
1. Fetch     ← FetchForWritingByTags<View>(query)   // boundary locked at this position
2. Decide    ← view.EnsureSomething()               // throws if invariant violated
3. Append    ← session.Events.Append(tagged event)  // tagged with same key
4. Save      ← SaveChangesAsync                     // throws DcbConcurrencyException
              if a matching event landed since step 1
```

The aggregation is incidental; the *position* is the product.

#### One event, multiple boundaries

A single domain event can join *more than one* DCB fold by carrying
multiple tags. [`SubmitCart`](../CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs)
shows this: `CartSubmitted` is per-cart, but a cart can hold many SKUs, so
the handler tags the event with every `Sku` in `cart.Lines.Distinct()` at
append time. Each per-SKU `InventoryView` fold sees the same event and
updates its own state — converting that cart's pending reservation into a
stock deduction (`Stock -= qty`, `Reserved -= qty`).

This is what makes DCB compose: the tag namespace is shared across
events, but each fold consumes only the tags it queries for. Multi-tagging
on the write side means one event can participate in arbitrarily many
cross-stream boundaries without invention.

---

## Projection lifecycles

Marten projections come in three lifecycles. Pick by the reason in the
"Why this lifecycle" column — not by habit.

| Lifecycle | Example | Why this lifecycle |
|---|---|---|
| **Inline snapshot** (aggregate) | [`CartAggregate`](../CartShop.Core/Domain/CartAggregate.cs) | Read-after-write for [`GetCart`](../CartShop.Core/Feature/Cart/Queries/GetCart/Handler.cs); aggregate is its own read model |
| **Inline custom projection** | [`OpenCartByCustomer`](../CartShop.Core/Domain/OpenCartByCustomer.cs) | Uniqueness check across streams; [`CreateCart`](../CartShop.Core/Feature/Cart/Commands/CreateCart/Handler.cs) must see the previous commit |
| **Async** | [`SalesByDay`](../CartShop.Core/Domain/SalesByDay.cs) | Lag-tolerant rollup across every cart; avoids write-path contention |
| **Live** | [`CartTimeline`](../CartShop.Core/Feature/Cart/Queries/CartTimeline/Handler.cs) | Rarely queried; storing would duplicate the event log; replay on demand is cheap |

Registration in [`Initialization.cs`](../CartShop.Core/Initialization.cs):

```csharp
opts.Projections.Snapshot<CartAggregate>(SnapshotLifecycle.Inline);
opts.Projections.Add<OpenCartByCustomerProjection>(ProjectionLifecycle.Inline);
opts.Projections.Add<SalesByDayProjection>(ProjectionLifecycle.Async);
// CartTimeline: no registration — it's built in the query handler from FetchStreamAsync
```

`Snapshot<T>` is sugar for a `SingleStreamProjection<T>` where the
Apply methods live on the aggregate itself. The two `Add<...>` calls are the
general form.

> **Marten 9 note.** `OpenCartByCustomerProjection` and `SalesByDayProjection`
> are declared `partial` — Marten 9 dispatches their `Apply`/`Create` methods
> via a compile-time source generator that emits the other half of the class.
> `CartAggregate` (registered via `Snapshot<T>`) is self-aggregating and needs
> no `partial`. Omitting `partial` on a convention-method projection subclass
> throws `InvalidProjectionException` at host startup.

### Decision rule

Walk down this list and stop at the first "yes":

1. **Does a later request read this projection and need the previous commit?** → **Inline**.
2. **Is the projection cheap (a few field updates) and you'd just like it consistent?** → **Inline** is fine.
3. **Is it expensive (see below) or just a rollup that tolerates lag?** → **Async**.
4. **Rarely queried, low per-stream event count?** → **Live**.

---

## Appendix: when a projection should NOT be inline

Inline lifecycle pays the projection cost on every write, in the same
transaction as the events. That's correct for small, cheap folds — and a
trap for everything else. The five categories that should default to async:

### 1. Network I/O per event
The projection calls an external service to enrich the document.
> *Example:* a `CartWithTax` view that on every `ItemAdded` calls a tax-rate
> API by postal code. Each event triggers an HTTPS round-trip (100–500 ms).
> Inline → every AddItem waits for the API. Async → users don't notice.

**Rule of thumb:** if a projection touches anything outside the local DB
transaction, it must be async (or live).

### 2. Heavy computation
CPU-bound work that scales with history.
> *Example:* a `CustomerSegmentation` projection that on every `CartSubmitted`
> recomputes the customer's RFM (Recency / Frequency / Monetary) score from
> full history and assigns a segment label. Ten–twenty ms per event adds up.

### 3. Fan-out projections
One event produces many rows.
> *Example:* a `ProductCoOccurrenceMatrix` for "customers who bought X also
> bought Y." Each `CartSubmitted` writes one row for *every pair* of products
> in the cart — a 10-item cart writes 45 rows. Cost is quadratic in cart size.

### 4. Cross-aggregate queries
The projection itself reads other documents to compute its values.
> *Example:* a `CartAffordability` view that on every `ItemAdded` loads the
> customer's wallet balance from another bounded context. N+1 reads on the
> write path, plus a real consistency hazard between contexts.

### 5. Big payloads
The doc is large and gets rewritten in full on every event.
> *Example:* a `CustomerActivityLog` document holding the last 1000 events
> as embedded JSON — every new event rewrites the whole document. Write cost
> grows with history. (The deeper fix here is usually to redesign the doc.)

If the projection fits any of these, default to async and pay the
operational cost of running the daemon. Inline is operationally simpler
but makes every write proportionally slower.
