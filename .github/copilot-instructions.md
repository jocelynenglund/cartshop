# Copilot instructions for CartShop

CartShop is a teaching reference for event sourcing + DCB (Dynamic
Consistency Boundary) on Marten 8 / Wolverine 5 / .NET 10 Aspire +
Angular 21. Code is organized as vertical slices: one file per command
or query under `CartShop.Core/Feature/<Aggregate>/(Commands|Queries)/<Name>/Handler.cs`,
with the request/response DTOs and the Wolverine.Http attribute all
colocated.

## Guided walkthrough

If the user asks for a **tour**, **walkthrough**, **intro**, or to be
**taught the repo** — or simply says *"teach me"* — read
[`TeachMe.md`](../TeachMe.md) at the repo root and follow it as a
syllabus. It contains the nine-step path, the agent pacing rules
("walk one step, quiz, wait for questions"), and the progress-tracking
protocol (state file at `~/.cartshop-teachme.json`).

Don't dump the syllabus on the user. Pace it.

## Codebase conventions

- One slice = one file. Don't introduce controllers, MediatR, or a
  central route table.
- Aggregates own their invariants via decision methods that return
  events (`CartAggregate.Submit()`, `InventoryView.EnsureCanReserve()`).
- Handlers shrink to **load → decide → append → save**.
- Domain rule violations are typed exceptions caught at the HTTP edge;
  no `Result<T, E>` plumbing.
- Events are plain `record` types in `CartShop.Events`. Field changes:
  add with defaults, drop if unused, never rename in place — prefer a
  new event type for semantic changes.

## Background reading

For deeper context before answering tradeoff questions:

- [`README.md`](../README.md) — DCB story, slice pattern.
- [`docs/PATTERNS.md`](../docs/PATTERNS.md) — projection lifecycles
  (inline / async / live), the four-step DCB pattern, and the five
  "don't use inline" categories.
- [`CLAUDE.md`](../CLAUDE.md) — team conventions for adding new slices.
