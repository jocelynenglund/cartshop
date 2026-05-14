# Teach Me: CartShop

**Audience:** an AI agent guiding a human learner through this codebase
interactively. Not a tutorial for a human to read top-to-bottom — it's
instructions to the agent on how to pace, what to show, and what to ask.

## How to use this file

You are a tour guide, not a lecturer. Pace yourself to the learner.

- **Walk one step at a time.** After each step, stop and ask if they
  have questions before moving on. Don't dump the whole syllabus.
- **Show, don't tell.** Open the file in question. Quote the lines that
  matter. Skip the rest.
- **Test understanding before advancing.** Each step has a small quiz —
  ask it, wait for their answer, only continue once they get it (or
  acknowledge they're stuck and want you to explain).
- **Match depth to interest.** If they're sharp on event sourcing, skim
  the early steps. If they're new to .NET, expand on what `Marten` and
  `Wolverine` do before going further.
- **Don't claim done until they're done.** The walkthrough is finished
  when the learner says it is, not when you've covered every step.

If the learner runs the app at any point, they need:

```bash
dotnet run --project CartShop.AppHost
```

(First run also needs `cd cart-shop-web && npm install`.)

---

## Step 1 — The big picture

Open [`README.md`](README.md). Read it together, top to about the
"Layout" section.

Explain in your own words:

- **Vertical slices.** Each feature is one file under
  `CartShop.Core/Feature/<Aggregate>/(Commands|Queries)/<Name>/Handler.cs`.
  No MediatR, no controllers, no central route table — Wolverine.Http
  discovers endpoints by attribute.
- **Event sourcing.** State lives as a stream of events, not rows.
  Aggregates rebuild themselves by replaying.
- **The cart vs. inventory split.** Two consistency boundaries. Cart =
  one stream per cart, classic aggregate. Inventory = a *tag query*
  across many cart streams. That's the interesting part.

**Quiz:** "If I add an item to my cart, what kind of consistency check
needs to happen — single-stream or cross-stream?" *(Answer: cross-stream
— inventory spans every cart, so single-cart enforcement isn't enough.
This sets up DCB.)*

## Step 2 — One read

Open
[`CartShop.Core/Feature/Cart/Queries/GetCart/Handler.cs`](CartShop.Core/Feature/Cart/Queries/GetCart/Handler.cs).

It's ~20 lines. Walk them through:

- `[WolverineGet("/api/carts/{cartId}")]` — route binding via attribute.
- `IQuerySession` — read-only Marten session.
- `session.LoadAsync<CartAggregate>(cartId)` — single document load. Not
  a stream replay. The aggregate is an **inline snapshot**, kept fresh
  on every write.

Open [`CartShop.Core/Initialization.cs`](CartShop.Core/Initialization.cs)
and show the registration:

```csharp
opts.Projections.Snapshot<CartAggregate>(SnapshotLifecycle.Inline);
```

Inline = the snapshot updates in the same transaction as the events
that wrote it. That's why `LoadAsync` returns the latest state after a
write — no daemon, no lag.

**Quiz:** "What would change if we made the snapshot lifecycle `Async`
instead of `Inline`?" *(Answer: the snapshot would lag the write by
however long the daemon takes to catch up. `GetCart` immediately after
`AddItem` would sometimes return stale data.)*

## Step 3 — One write

Open
[`CartShop.Core/Feature/Cart/Commands/CreateCart/Handler.cs`](CartShop.Core/Feature/Cart/Commands/CreateCart/Handler.cs).

Walk through:

- Input validation at the top — empty/whitespace customer name.
- `CartAggregate.Create(cartId, customerName)` — a **static factory on
  the aggregate** that returns the `CartCreated` event. The aggregate
  owns the rule; the handler is just orchestration.
- `session.Events.StartStream<CartAggregate>(cartId, created)` — opens
  a new event stream keyed by the cart id.
- `SaveChangesAsync` — atomic write of the event(s) and any inline
  projection updates.

Now open
[`CartShop.Core/Domain/CartAggregate.cs`](CartShop.Core/Domain/CartAggregate.cs)
and show that `Create` is just a static method that throws
`CartRuleViolation` if the name is bad — and that the handler catches
that to return a `BadRequest`.

**Quiz:** "Why is `Create` on the aggregate as a *static* method
instead of an instance method?" *(Answer: there's no instance yet — the
cart doesn't exist until this event is appended. Static factory is the
DDD convention for that case.)*

## Step 4 — Decision methods on aggregates

Stay in `CartAggregate.cs`. Point at the four decision methods:
`Create`, `AddLine`, `RemoveLine`, `Submit`, `ApplyCoupon`.

Each:

1. Checks the rule.
2. Throws a typed exception if violated.
3. Returns the event(s) on success.

The handler's pattern is uniform: **load → decide → append → save**.
Rules live on the aggregate; orchestration lives in the handler.
Nothing about HTTP, sessions, or tagging leaks into the aggregate.

Open
[`CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs`](CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs)
as the cleanest example — it's eight lines of real work.

**Quiz:** "If I want to enforce 'a cart can have at most 50 items,'
where does that rule go?" *(Answer: as a check inside `CartAggregate.AddLine`,
not in the AddItem handler. The handler stays slim; new rules pile up
on the aggregate.)*

## Step 5 — Single-stream invariants

`SubmitCart` enforces *"cart not already submitted"* and *"cart not
empty."* Both rules fit inside one stream — the cart's own.

Open `Domain/CartAggregate.cs` → `Submit()` method. The state needed
to enforce the rule (`Status`, `Lines.Count`) is reconstructed by
replaying the cart's stream via `AggregateStreamAsync` in the handler.

Contrast with `Get`: where Get uses the cached inline snapshot (cheap),
Submit replays the stream (more expensive, but guarantees fresh state
at the moment of the write).

**Quiz:** "Why does `SubmitCart` replay the stream when there's an
inline snapshot available? Isn't that wasteful?" *(Answer: the snapshot
is for *reads*; for *writes that depend on current state*, you want the
stream's version number as the consistency check. `AggregateStreamAsync`
gives you that.)*

## Step 6 — DCB, first encounter

This is the headline of the codebase. Take your time.

Open
[`CartShop.Core/Feature/Cart/Commands/AddItem/Handler.cs`](CartShop.Core/Feature/Cart/Commands/AddItem/Handler.cs).

Read the comments. Walk through:

1. **The tag query** — `EventTagQuery.For(sku).AndEventsOfType<…>()`.
   This isn't asking about one cart's events; it's asking about *every
   event tagged with this SKU, across every cart stream*.
2. **`FetchForWritingByTags<InventoryView>(query)`** — Marten folds the
   matching events into an `InventoryView` *and locks the boundary at
   this position*. That last part is the magic.
3. **`inventory.EnsureCanReserve(quantity)`** — the inventory rule.
4. **`cart.AddLine(...)`** — the cart rule (not submitted, etc.).
5. **Tag the event with the SKU** before append: `tagged.AddTag(sku)`.
6. **`SaveChangesAsync`** — if any matching SKU-tagged event landed
   between step 2 and now, Marten throws `DcbConcurrencyException` and
   the write fails. The handler catches it and returns `409`.

Open [`CartShop.Core/Domain/InventoryView.cs`](CartShop.Core/Domain/InventoryView.cs).
Show the Apply methods and the bookkeeping table in the doc comment.

**Run the concurrent race demo** from the README ("Concurrent race —
the harder case" section). Two backgrounded curls, one stock unit. One
wins, one gets 409. That's the boundary firing.

**Quiz:** "Without DCB, how would two carts both reserving the last
item end up overselling?" *(Answer: each cart's handler reads inventory
independently, both see one unit available, both pass the check, both
append `ItemAdded`. No mechanism prevents the second one. DCB's tag
boundary fails the second `SaveChangesAsync`.)*

## Step 7 — DCB as a primitive, not a one-trick

Open
[`CartShop.Core/Feature/Cart/Commands/ApplyCoupon/Handler.cs`](CartShop.Core/Feature/Cart/Commands/ApplyCoupon/Handler.cs)
and [`CartShop.Core/Domain/CouponUsageView.cs`](CartShop.Core/Domain/CouponUsageView.cs).

Same shape as inventory:

```
1. Fetch     ← FetchForWritingByTags<View>(query)
2. Decide    ← view.EnsureSomething()
3. Append    ← session.Events.Append(tagged event)
4. Save      ← SaveChangesAsync (throws if boundary moved)
```

But the *rule* is different: inventory is a numeric balance ("can I
reserve N more?"); coupon usage is a one-shot claim ("has this code
been used yet?"). Same primitive, different invariant shape.

**Quiz:** "What two value types are registered as DCB tag types in
`Initialization.cs`?" *(Answer: `Sku` and `CouponCode`. Any event
carrying one of these properties gets auto-tagged and participates in
the matching tag query.)*

## Step 8 — One event, multiple boundaries

This is the subtlest part of the codebase and worth pausing on.

Open
[`CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs`](CartShop.Core/Feature/Cart/Commands/SubmitCart/Handler.cs)
and show:

```csharp
var tagged = new Event<CartSubmitted>(evt);
foreach (var sku in cart.Lines.Select(l => l.Sku).Distinct())
    tagged.AddTag(sku);
```

`CartSubmitted` is *one event*, written to *one cart stream*. But it
carries *multiple SKU tags* — one per unique SKU in the cart. Each
per-SKU `InventoryView` fold sees the same event independently.

Open `InventoryView.cs` → `Apply(CartSubmitted)`:

```csharp
if (ReservedByCart.TryGetValue(e.CartId, out var qty))
{
    Stock -= qty;
    ReservedByCart.Remove(e.CartId);
}
```

When the cart submits, that cart's pending reservation **moves from
Reserved into a Stock deduction**. The books are now honest: `Stock`
is what's physically left, `Reserved` is only what's still pending in
open carts.

**Quiz:** "If a cart contains 3 SKUs and submits, how many separate
DCB boundaries does the `CartSubmitted` event participate in?"
*(Answer: three — one per SKU. Each per-SKU fold sees the tag and
updates independently.)*

## Step 9 — Projection lifecycles

Open
[`CartShop.Core/Initialization.cs`](CartShop.Core/Initialization.cs)
and walk through the projection registrations one by one:

1. **`Snapshot<CartAggregate>(Inline)`** — the aggregate is its own
   read model. Updated in-txn.
2. **`Add<OpenCartByCustomerProjection>(Inline)`** — a *custom inline*
   projection that's not an aggregate. Used by `CreateCart` for the
   one-open-cart-per-customer uniqueness check; must see the previous
   commit, hence inline.
3. **`Add<SalesByDayProjection>(Async)`** — daily rollup, lag-tolerant,
   fan-in across every cart stream. Async daemon handles it.
4. **`CartTimeline`** — *not registered at all*. It's a **live**
   projection — built on demand in
   [`Feature/Cart/Queries/CartTimeline/Handler.cs`](CartShop.Core/Feature/Cart/Queries/CartTimeline/Handler.cs)
   via `FetchStreamAsync`. Nothing stored.

Open [`docs/PATTERNS.md`](docs/PATTERNS.md) and read the four-step
decision rule together. The five "don't use inline" categories at the
bottom are worth flagging — that's the trap to avoid.

**Quiz:** "I want to build a 'recently viewed products' read model.
Which lifecycle would you pick and why?" *(Answer: usually async or
live, depending on read volume. The list isn't a write-path
dependency, so inline is wasted cost.)*

## Step 10 — Schema evolution (short detour)

Once the patterns are absorbed, take a brief detour. Ask: *"What
happens if we drop a property from an event after some have been
written?"*

Walk through the rules of thumb from this codebase's history:

1. **Adding an optional field with a default** — edit the record in
   place. `record CouponApplied(Guid CartId, CouponCode Code,
   DateTimeOffset At, string Reason = "")` works.
2. **Dropping a field** — usually safe. JSON keeps the value
   unreachable; nothing breaks unless code still reads it.
3. **Renaming or repurposing** — *don't*. Add a new event type, leave
   the old one alone. Aggregates grow `Apply(Old)` AND `Apply(New)`.
4. **Semantic change** — new event type, often a new name. Old events
   stay honest about what they meant at the time.

Upcasting exists as an escape hatch but is rarely the right call;
prefer adding new event types.

**Quiz:** "I want to add a `PromotionCode` field to `ItemAdded` so old
events keep working. What's the safe move?" *(Answer: add the field
with a default value — `string PromotionCode = ""`. Old JSON has no
PromotionCode; deserialization uses the default. New writes include
it.)*

---

## Wrap-up

Once the learner has been through the steps that interest them, ask:

- "Want to try writing a new slice? A small one — e.g. `RenameCart` —
  takes maybe 30 minutes and exercises the full pattern."
- "Anything you'd want explained differently before you'd feel
  comfortable opening a PR?"

If they want to write a new slice, walk them through the *one-file
slice pattern* in `CLAUDE.md` — that's the team's canonical template.

End the walkthrough when they say they're done. Save them a session
summary with `/takenotes` if they have that skill installed.
