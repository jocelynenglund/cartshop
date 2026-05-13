# CartShop

Sample event-sourced cart submission app, built with the same vertical-slice
shape as `event-store-api` / `ChoreMonkey` / `getsafe`, but on top of
**Marten + Wolverine** instead of `FileEventStore + MediatR`.

Stack
- .NET 10
- Marten 8 (event store + inline `CartAggregate` snapshot projection)
- Wolverine 5 + Wolverine.Http (HTTP endpoint discovery)
- Wolverine.Marten (transactional outbox integration)
- .NET Aspire 13 (orchestration: Postgres + API + Angular)
- Angular 21 (standalone components, signals)

## Layout

```
CartShop.AppHost/          Aspire orchestrator (Postgres + api + web)
CartShop.ServiceDefaults/  Shared Aspire defaults (OTel, health, discovery)
CartShop.ApiService/       Web host — Program.cs
CartShop.Core/             Vertical slices live here
  Domain/
    CartAggregate.cs       Inline snapshot built from cart events
    InventoryView.cs       Tag-folded live aggregate (DCB)
  Feature/Cart/
    Commands/CreateCart/Handler.cs
    Commands/AddItem/Handler.cs      ← DCB stock check
    Commands/RemoveItem/Handler.cs
    Commands/SubmitCart/Handler.cs
    Queries/GetCart/Handler.cs
    Queries/ListSubmittedCarts/Handler.cs
  Feature/Inventory/
    Commands/SetStock/Handler.cs
    Queries/StockLevel/Handler.cs
  Initialization.cs        Marten + Wolverine + tag-type registration
CartShop.Events/           Plain event records + Sku tag type
cart-shop-web/             Angular SPA with mirrored slice folders
  src/app/feature/
    cart/{commands,queries}/<slice>/<slice>.ts
    inventory/{commands,queries}/<slice>/<slice>.ts
  src/app/app.ts           Pure composition over slice components
```

## Slice pattern

Each slice is a single file with the command/request record + a static endpoint
with a `[WolverinePost|Get|Delete]` attribute. Wolverine.Http discovers the
attribute and registers it as a minimal-API route at startup.

```csharp
public static class CreateCartEndpoint
{
    [WolverinePost("/api/carts")]
    public static async Task<IResult> Handle(
        CreateCartRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        var cartId = Guid.NewGuid();
        var created = new CartCreated(cartId, request.CustomerName, DateTimeOffset.UtcNow);
        session.Events.StartStream<CartAggregate>(cartId, created);
        await session.SaveChangesAsync(ct);
        return Results.Created($"/api/carts/{cartId}", new CreateCartResponse(cartId));
    }
}
```

`CartAggregate` is registered as an inline snapshot projection, so reads use
`session.LoadAsync<CartAggregate>(id)` (cheap document load) and the
"submitted carts" list query is a regular LINQ query against the same
projection.

## Running

```bash
dotnet run --project CartShop.AppHost
```

Aspire spins up:
- `postgres` (with pgAdmin sidecar)
- `cartdb` database
- `api` (.NET 10 API)
- `web` (Angular dev server on port 4200, proxied to `api` for `/api/*`)

The Aspire dashboard prints a URL on startup; the `web` resource link opens the
SPA.

First run will need npm deps:

```bash
cd cart-shop-web && npm install
```

## API surface

| Verb   | Path                                  | Slice                |
|--------|---------------------------------------|----------------------|
| POST   | `/api/carts`                          | CreateCart           |
| GET    | `/api/carts/{id}`                     | GetCart              |
| POST   | `/api/carts/{id}/items`               | AddItem (DCB)        |
| DELETE | `/api/carts/{id}/items/{itemId}`      | RemoveItem           |
| POST   | `/api/carts/{id}/submit`              | SubmitCart           |
| GET    | `/api/carts/submitted`                | ListSubmittedCarts   |
| POST   | `/api/inventory/{sku}`                | SetStock             |
| GET    | `/api/inventory/{sku}`                | StockLevel           |

`GET /openapi/v1.json` returns the live schema in development.

## DCB inventory

`Sku` is registered as a Marten tag type:

```csharp
opts.Events.RegisterTagType<Sku>();
```

Events carry the Sku as a strong-typed property and are explicitly tagged at
append time by wrapping them as `IEvent`:

```csharp
var tagged = new Event<ItemAdded>(evt);
tagged.AddTag(sku);
session.Events.Append(cartId, tagged);
```

`AddItem` reads the inventory through the **DCB boundary** so the
`SaveChangesAsync` call asserts no other Sku-tagged event slipped in between
the read and the write — across every cart, atomically:

```csharp
var query = EventTagQuery
    .For(sku)
    .AndEventsOfType<ProductStockSet, ItemAdded, ItemRemoved>();

IEventBoundary<InventoryView> boundary =
    await session.Events.FetchForWritingByTags<InventoryView>(query, ct);

if (boundary.Aggregate.Available < request.Quantity)
    return Results.BadRequest(...);

session.Events.Append(cartId, tagged);
try   { await session.SaveChangesAsync(ct); }
catch (DcbConcurrencyException) { return Results.Conflict(...); }
```

`InventoryView` is a live fold of `ProductStockSet` / `ItemAdded` / `ItemRemoved`
across every stream — no per-stream snapshot needed.
