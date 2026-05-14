# docs/

Where this repo's event-model lives, how to view it, and how to regenerate it.

## What's in here

| File | Purpose |
|---|---|
| [`event-model.png`](event-model.png) | The rendered diagram embedded in the root README. Re-export after canvas edits. |
| [`CartShop_DCB_Inventory-2026-05-14.json`](CartShop_DCB_Inventory-2026-05-14.json) | Current Nebulit canvas source. Import this into the canvas to view/edit. |
| [`PATTERNS.md`](PATTERNS.md) | Patterns catalog (slices, DCB, projection lifecycles, the five "don't use inline" categories). |

## Viewing the model

Open the Nebulit canvas in a browser:

→ **https://app.eventmodelers.de/canvas**

Use the canvas's import action (top-right toolbar) and select
[`CartShop_DCB_Inventory-2026-05-14.json`](CartShop_DCB_Inventory-2026-05-14.json).

The board appears with all rows, columns, sticky notes, edges, and the
given/when/then scenarios. Read it left-to-right as a timeline of slices.

> **Each import creates a new board.** If you've already imported a prior
> version, delete that board first in Nebulit before importing — otherwise
> you'll have duplicates.

## Re-exporting the PNG

After canvas edits, use the canvas's export action and save the PNG over
[`event-model.png`](event-model.png). The root README's `![Event model](docs/event-model.png)`
embed picks up the new image on the next push.

## Regenerating the JSON from source

The JSON isn't hand-edited — it's generated from a compact YAML spec via
the `event-model-to-nebulit` Claude skill. The source YAML lives outside
this repo (in `/data/projects/event-model-notation/examples/cartshop.yaml`
on the author's machine) so the generator can be reused across projects.

To regenerate after editing the YAML:

```bash
python3 ~/.claude/skills/event-model-to-nebulit/generate.py \
    /path/to/cartshop.yaml \
    docs/CartShop_DCB_Inventory-2026-05-14.json
```

Then re-import into the canvas (deleting the previous board first), reposition any
new slices if needed, and re-export the PNG.

## Edge color convention

When reading the diagram:

Three colors, three meanings:

- 🔴 **Red, `!` caption — Gate.** The command on the receiving end must
  consult the read model and pass the rule before its event can append.
  Used on `StockLevel (DCB) → AddItem`, `CouponUsage (DCB) → ApplyCoupon`,
  and `OpenCartByCustomer → CreateCart`. (Nebulit's `!` natively flags
  "unmapped properties in dependencies" — we knowingly overload it to
  mean "gate." Same symbol, broader meaning in this repo.)
- 🟠 **Orange — DCB feed.** Events whose tags participate in the DCB
  boundary, folding into the read model. Distinct from a gate (these
  don't block a write) and from ordinary fan-out (they're load-bearing
  for the consistency check).
- 🔵 **Blue/gray — ordinary.** Plain projection fan-out into a
  non-DCB read model. No consistency stake; eventual consistency or
  read-only display.

Scenarios in the Spec Lane row with a **red border** are `expectError`
cases ("this should fail"). Plain-bordered scenarios are happy-path or
state-update assertions.
