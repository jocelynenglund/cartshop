# CartShop

A small, working **DCB (Dynamic Consistency Boundary)** reference, built as a
vertical-slice event-sourced cart on Marten 8 + Wolverine 5 + .NET Aspire +
Angular. Intended as a teaching example: the slices are deliberately thin so
the consistency story is the visible thing.

Stack
- .NET 10
- Marten 8 — event store, inline snapshot projection, DCB tag tables
- WolverineFx 5 — `[WolverinePost|Get|Delete]` HTTP endpoint discovery
- WolverineFx.Marten — transactional outbox
- .NET Aspire 13 — orchestrates Postgres + API + Angular
- Angular 21 — standalone components, signals

## Event model

The Nebulit canvas under [`docs/`](docs/CartShop_DCB_Inventory-2026-05-13.json)
captures the full flow. The key shape is the red `!` edge from
`StockLevel (DCB)` into `AddItem`: that's the cross-stream consistency check.

![Event model — DCB cart submission](docs/event-model.png)

Read the diagram top-down per column:

- **Actor row** — who initiates each command.
- **Interaction row** — commands (blue) and read models (green).
- **Cart Events** swimlane — events written to a per-cart stream.
- **Inventory Events** swimlane — `ProductStockSet` lives in its own
  `product-{sku}` stream, in a different swimlane to make the cross-stream
  nature obvious.
- **Spec Lane** — notes anchoring how the DCB tag+boundary plumbing actually
  works.

The interesting node is `StockLevel (DCB)`. It's a *live fold* over every event
tagged with the same SKU, regardless of which stream the event came from:

```
EventTagQuery.For(sku)
  .AndEventsOfType<ProductStockSet, ItemAdded, ItemRemoved>()
```

`AddItem` reads it through Marten's DCB boundary, and `SaveChangesAsync` throws
`DcbConcurrencyException` if any other Sku-tagged event lands between the read
and the write.

Compare with `GetCart` (col 5): no `!` edge, because it's a Marten **inline
snapshot** built from a single stream — the stream's own version number is the
consistency boundary, so no cross-stream check is needed.

## Layout

```
CartShop.AppHost/          Aspire orchestrator (Postgres + api + web)
CartShop.ServiceDefaults/  Shared Aspire defaults (OTel, health, discovery)
CartShop.ApiService/       Web host — Program.cs
CartShop.Core/             Vertical slices live here
  Domain/
    CartAggregate.cs       Inline snapshot built from one cart's stream
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

Each slice is a single file: the request/response records colocated with a
static endpoint class. Wolverine.Http discovers the attribute and registers it
as a minimal-API route at startup — no controllers, no DI registration, no
endpoint mapping table.

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
projection document.

The Angular SPA mirrors this layout one-to-one: every backend slice has a
matching `.ts` file under `cart-shop-web/src/app/feature/...`, each owning its
DTOs, HTTP call, template, and styles. The root `App` component is pure
composition — no shared cart service, no global store.

## DCB inventory — the punchline

`Sku` is registered as a Marten tag type:

```csharp
opts.Events.RegisterTagType<Sku>();
```

Events that carry a Sku are explicitly tagged at append time by wrapping them
as `IEvent`:

```csharp
var tagged = new Event<ItemAdded>(evt);
tagged.AddTag(sku);
session.Events.Append(cartId, tagged);
```

`AddItem` reads the inventory through the **DCB boundary** so
`SaveChangesAsync` asserts no other Sku-tagged event slipped in between the
read and the write — across every cart, atomically:

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

`InventoryView` is a live fold of `ProductStockSet` / `ItemAdded` /
`ItemRemoved` across every stream — no per-stream snapshot needed, and no
ad-hoc lock or queue. The consistency comes from the tag boundary alone.

## Running

```bash
dotnet run --project CartShop.AppHost
```

Aspire spins up:
- `postgres` (with pgAdmin sidecar)
- `cartdb` database
- `api` (.NET 10 API)
- `web` (Angular dev server on port 4200, proxied to `api` for `/api/*`)

The Aspire dashboard prints a URL on startup; the `web` resource link opens
the SPA. First run also needs npm deps:

```bash
cd cart-shop-web && npm install
```

If you prefer Podman to Docker:

```bash
export DOTNET_ASPIRE_CONTAINER_RUNTIME=podman
systemctl --user start podman.socket
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

## End-to-end DCB walk-through

```bash
# Establish stock
curl -X POST localhost:5270/api/inventory/SKU-A -H 'content-type: application/json' \
     -d '{"quantity":10}'

# Cart 1 reserves 8 — OK; available drops to 2
CART1=$(curl -sX POST localhost:5270/api/carts \
        -H 'content-type: application/json' \
        -d '{"customerName":"Alice"}' | jq -r .cartId)
curl -X POST localhost:5270/api/carts/$CART1/items \
     -H 'content-type: application/json' \
     -d '{"sku":"SKU-A","quantity":8,"unitPrice":1.5}'

# Cart 2 tries to reserve 5 — REJECTED (cross-cart, only 2 available)
CART2=$(curl -sX POST localhost:5270/api/carts \
        -H 'content-type: application/json' \
        -d '{"customerName":"Bob"}' | jq -r .cartId)
curl -X POST localhost:5270/api/carts/$CART2/items \
     -H 'content-type: application/json' \
     -d '{"sku":"SKU-A","quantity":5,"unitPrice":1.5}'
# → 400 { "error": "Insufficient stock", "requested": 5, "available": 2 }
```

That rejection is the DCB boundary doing its job: two independent cart streams,
one atomic inventory invariant.
