# docs/

Where this repo's event-model lives and how to view and update it.

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

## Edge color convention

Two colors:

- 🟠 **Orange** — events and gates touching a DCB read model. Events
  whose tags fold into the view, plus the gate edges from the view into
  the command it protects. Gate edges carry a `!` caption to mark
  "before this command can write, it must consult the view first."
  (Nebulit's `!` natively flags *"unmapped properties in dependencies"* —
  we knowingly overload it to mean "gate.")
- 🔵 **Blue/gray** — ordinary projection fan-out into a non-DCB read
  model. No consistency stake; eventual consistency or read-only display.

Scenarios in the Spec Lane with a **red border** are `expectError`
cases ("this should fail"). Plain-bordered scenarios are happy-path or
state-update assertions.
