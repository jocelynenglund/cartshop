# CartShop — Claude notes

Event-sourced cart sample on **Marten + Wolverine + Aspire + Angular 21**.
Built as a teaching reference for the Dynamic Consistency Boundary (DCB)
pattern.

## Architectural rules

- One slice = one file under `CartShop.Core/Feature/<Aggregate>/<Commands|Queries>/<Name>/Handler.cs`.
- A slice file holds: request DTOs, response DTOs, and a static endpoint class
  with `[WolverinePost|Get|Delete]` on the `Handle` method.
- No MediatR. No controllers. Wolverine.Http discovers endpoints by attribute.
- Domain events live in `CartShop.Events` as plain `record` types.
- `CartAggregate` rebuilds via `Apply(event)` methods and is registered as an
  **inline snapshot projection** — reads are document loads, not stream rebuilds.
- Validation is inline at the top of each handler; bad requests return
  `Results.BadRequest`. No FluentValidation.
- `Initialization.AddCartShopCore` wires Marten + Wolverine + projections; it
  takes the Aspire connection name (`"cartdb"`) so the host stays declarative.

## When adding a new slice

1. Add the event record(s) in `CartShop.Events`.
2. Add a matching `Apply(NewEvent e)` to `CartAggregate`.
3. Create `Feature/<Aggregate>/Commands/<Name>/Handler.cs` with the
   `[WolverinePost("/api/...")]` static handler.
4. That's it — no DI registration, no endpoint mapping. Wolverine discovers it.

## Running

```bash
dotnet run --project CartShop.AppHost
```

First time also: `cd cart-shop-web && npm install`.

## Conventions to keep

- `IDocumentSession` for writes, `IQuerySession` for reads. Tag/DCB methods
  (`FetchForWritingByTags`, `AggregateByTagsAsync`) are only on `IDocumentSession`.
- Use `session.Events.StartStream<CartAggregate>(id, ...)` to start a stream,
  `session.Events.Append(id, ...)` to append.
- For commands that need current state, load via
  `session.Events.AggregateStreamAsync<CartAggregate>(id)` (rebuild) when
  enforcing invariants, or `session.LoadAsync<CartAggregate>(id)` when the
  inline projection is sufficient.
- For invariants spanning multiple streams (e.g. cross-cart inventory), use
  the DCB pattern: register the tag type **coupled to its view**
  (`opts.Events.RegisterTagType<Sku>().ForAggregate<InventoryView>()`), wrap
  events as `new Event<T>(raw).AddTag(tagValue)`, and
  `FetchForWritingByTags<View>(query)` inside the slice. The
  `SaveChangesAsync` call enforces the boundary.
- Aspire connection name lives in `Program.cs` and `AppHost.cs` — keep them in
  sync if renaming.

## Marten 9 source generation (read before adding an aggregate or DCB view)

Marten 9 removed runtime code generation. Every type with `Apply` / `Create` /
`ShouldDelete` convention methods is dispatched by a **compile-time source
generator** (`JasperFx.Events.SourceGenerator`), with **no runtime fallback** —
a missing dispatcher throws `InvalidProjectionException`, and it only surfaces
at runtime (startup for registered projections, request time for DCB views),
never at compile time. Three rules keep you out of that trap:

1. **Convention-method projection subclasses must be `partial`.** A
   `SingleStreamProjection<T>` / `MultiStreamProjection<T>` subclass with
   `Apply`/`Create`/`ShouldDelete` methods (e.g. `OpenCartByCustomerProjection`,
   `SalesByDayProjection`) needs `partial` so the generator can emit the other
   half of the class. A self-aggregating type registered via `Snapshot<T>`
   (e.g. `CartAggregate`) does **not** need `partial`.
2. **DCB view aggregates must be discoverable by the generator.** A plain
   view used only through `FetchForWritingByTags<T>` / `AggregateByTagsAsync<T>`
   (e.g. `InventoryView`, `CouponUsageView`) is never registered as a
   projection, so the generator can't see it unless you tell it. Mark
   identity-less views (no `Id`) with `[BoundaryAggregate]`
   (`JasperFx.Events.Aggregation`) **and** couple the tag to the view with
   `RegisterTagType<Tag>().ForAggregate<View>()`.
3. **Wolverine handlers** rely on the same codegen extraction: the host project
   references `WolverineFx.RuntimeCompilation` so `TypeLoadMode.Dynamic` keeps
   working. Don't remove it without switching to static codegen.

See [`docs/UPGRADE-marten9-wolverine6.md`](docs/UPGRADE-marten9-wolverine6.md)
for the full breaking-change ledger from the Marten 8 / Wolverine 5 upgrade.

## Frontend mirror

The Angular app under `cart-shop-web/src/app/feature/` mirrors the backend
folder layout one-to-one. Each frontend slice is a single TypeScript file
containing:
- DTO interfaces (mirroring the request/response records on the backend)
- A standalone Angular component that injects `HttpClient` and makes its own
  HTTP call
- Inline `template` and `styles`

The root `App` component does pure composition — no global cart service, no
shared store. State flows via `input()` / `output()` between sibling slices,
exactly like the backend has no shared command bus, just colocated handlers.
