# Upgrade: Marten 8 → 9, Wolverine 5 → 6

What it took to move CartShop from **Marten 8.37 / WolverineFx 5.39** to
**Marten 9.7.3 / WolverineFx 6.7.0** (the "Critter Stack 2026" majors).

## The one-line takeaway

The package bump **compiles with zero source changes** — but compiling clean is
not the same as working. Marten 9 removed runtime code generation, and three of
the four real breaking changes are **invisible to the compiler**: they only fire
at runtime. Run the app and exercise every path before calling an upgrade done.

## Versions

| Package | Before | After |
|---|---|---|
| `Marten` | 8.37.0 | 9.7.3 |
| `WolverineFx.Http` | 5.39.0 | 6.7.0 |
| `WolverineFx.Marten` | 5.39.0 | 6.7.0 |
| `WolverineFx.RuntimeCompilation` | — | 6.7.0 (new) |

Target framework stays `net10.0` (both majors dropped .NET 8). The project was
already on the `JasperFx.Events.*` namespaces from the 8/5 split, so those
`using`s did not change.

## The breaking-change ledger

| # | Change | Caught by | Symptom | Fix |
|---|---|---|---|---|
| 1 | Package majors | compiler | — (clean) | bump 4 versions |
| 2 | Convention-method **projections need `partial`** | **runtime — host startup** | `InvalidProjectionException: No source-generated dispatcher found for OpenCartByCustomerProjection` | mark `partial` |
| 3 | Wolverine 6 **extracted runtime codegen** | **runtime — host startup** | `Wolverine is running in TypeLoadMode.Dynamic ... no IAssemblyGenerator registered` | add `WolverineFx.RuntimeCompilation` |
| 4 | DCB **views need `[BoundaryAggregate]` + `.ForAggregate<T>()`** | **runtime — request time** | DCB reads 500 with the same `InvalidProjectionException` for `InventoryView`; writes succeed | register boundary aggregates |

> Plus an **incidental, non-upgrade** issue: a persisted Postgres data volume
> from an earlier run held a random password that no longer matched the
> hardcoded `cartshop-dev`. `POSTGRES_PASSWORD` is only honored on first
> initialization, so the fix was to reset the volume
> (`podman volume rm cartshop-pgdata`). Documented in `AppHost.cs`.

## Detail

### 2 & 4 — Marten 9 source generation (the big one)

Marten 9 dispatches all `Apply` / `Create` / `ShouldDelete` convention methods
through the compile-time `JasperFx.Events.SourceGenerator`. **There is no
runtime fallback.** The generator emits a dispatcher only for types it can
discover:

- **Registered projections** (`Snapshot<T>`, `Projections.Add<T>`) and
  self-aggregating snapshots are discovered automatically. `CartAggregate`
  (registered via `Snapshot<T>`) needed no change.
- **Convention-method projection subclasses** must be `partial` so the
  generator can emit the other half of the class:

  ```csharp
  public partial class OpenCartByCustomerProjection : SingleStreamProjection<OpenCartByCustomer, Guid> { ... }
  public partial class SalesByDayProjection       : MultiStreamProjection<SalesByDay, string> { ... }
  ```

- **DCB view aggregates** (`InventoryView`, `CouponUsageView`) are used only via
  `FetchForWritingByTags<T>` / `AggregateByTagsAsync<T>` and are never
  registered as projections, so the generator can't see them. They are
  identity-less (no `Id`), so they get the explicit opt-in marker **and** the
  tag is coupled to the view:

  ```csharp
  using JasperFx.Events.Aggregation;

  [BoundaryAggregate]
  public class InventoryView { /* Apply(...) methods */ }
  ```
  ```csharp
  // Initialization.cs
  opts.Events.RegisterTagType<Sku>().ForAggregate<InventoryView>();
  opts.Events.RegisterTagType<CouponCode>().ForAggregate<CouponUsageView>();
  ```

  Note the registration changed from the v8 form `RegisterTagType<Sku>()` —
  the `.ForAggregate<T>()` chain is what wires the view into source generation.

To verify the generator emitted a dispatcher for a type, build with
`-p:EmitCompilerGeneratedFiles=true` and look for
`obj/generated/JasperFx.Events.SourceGenerator/.../*Evolver.g.cs`.

### What did *not* change

The DCB write API is unchanged — `new Event<T>(raw)`, `.AddTag(tag)`,
`EventTagQuery.For(...).AndEventsOfType<...>()`, `EventAppendMode.Quick`,
`session.Events.Append(...)`, and `DcbConcurrencyException` all compile and
behave as before. Only the **read/fold side** needed the boundary registration.

### 3 — Wolverine 6 runtime codegen

Core `WolverineFx` no longer ships the Roslyn compiler. The default
`TypeLoadMode.Dynamic` (compile handlers at runtime) throws at startup unless
you either reference `WolverineFx.RuntimeCompilation` (auto-registers the
generator) or pre-generate with `dotnet run -- codegen write` and set
`TypeLoadMode.Static`. We chose the package for the like-for-like dev loop.

### Behavioral defaults to keep in mind (didn't bite us, could bite you)

- **Default serializer flips Newtonsoft → System.Text.Json.** Existing events
  written by Newtonsoft can fail to deserialize. We reset the DB to a fresh
  volume, so all events are STJ from the start — if you keep existing data, pin
  `opts.UseNewtonsoftForSerialization()` (needs `Marten.Newtonsoft`) or test a
  migration first.
- **`Events.UseIdentityMapForAggregates` defaults to `true`** — assumes
  decider-style aggregates. `CartAggregate` uses mutating `Apply()` methods;
  the full reserve→submit lifecycle was re-tested and the inventory/total math
  is correct, but this is the behavioral change to watch if you see cross-batch
  state bleed. One-line reverts exist: `opts.RestoreV8Defaults()` (Marten) and
  `opts.RestoreV5Defaults()` (Wolverine).

## How it was validated

Full DCB lifecycle exercised against the running app on Postgres:

| Step | Result |
|---|---|
| set stock 10 → read | `stock 10, reserved 0, available 10` |
| add 3 (`FetchForWritingByTags`) → read | `stock 10, reserved 3, available 7` |
| submit (total 29.97) → read | `stock 7, reserved 0, available 7` |
| over-reserve (5 of 2) | `400 Insufficient stock` (clean, not 500) |
| coupon apply then reuse | `200` then `409 already used` |

Both boundary aggregates, the cross-stream invariants, and the inline snapshot
all behave correctly on the new majors.
