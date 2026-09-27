## Target

src/ARIA.Core/Runtime/ShowController.cs (1627 lines) — split into cohort files

Снимок от 2026-09-15. Цель выросла до 2196 строк; решение по сплиту —
ADR 0009 (отклонён 2026-09-14, перепроверен 2026-09-27).

## Dependents (30)

- src/ARIA.App/AppHost.cs: only caller in prod (ctor, via ICommandBus seam)
- tests: 29 files via Harness/bus public interface (IShowHandler), zero touch internals

## Affected Stories

- e01 epic (specs/epics/e01-stabilization-trigger/epic.yaml), all 7 stories passing

## Test Coverage

- tests/ARIA.Core.Tests (178), ARIA.App.Tests (257), Persistence (21), Audio (70), Remote (59):
  all through public seams (verified 2026-09-15, full suite green)
- Gap: zero tests pin internal layout; any split keeps suite green by construction

## Risk: Medium

External risk Low (one prod caller, stable seam); internal risk High (all cohorts share _current/_status/_queue/_tracks/_preRolled/_retired — extract-class needs god-context).

## Recommended action

Partial-class file split only (mechanical, reversible); no behavior extract. Full extract-class rejected: shallow seam, shared mutable state, no forcing function.
