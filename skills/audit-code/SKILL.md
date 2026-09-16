---
name: audit-code
description: Audits C# code for defects, boundary violations, and convention drift. Use when user requests code review, audit, quality check, or mentions conventions, warnings, or architecture boundaries.
model: sonnet
---

# Audit Code

## Quick start

DO read target files fully before judging any line.
DO run `dotnet build` and capture warning count first.
ALWAYS quote violations as `file:line` with exact rule name.

## Workflows

### Scenario A — Defect hunt

DO trace every caller of touched functions first.
MUST fix root cause in shared function, NEVER per caller.
DO confirm fix with existing tests or one minimal reproduction.

### Scenario B — Boundary check

MUST route control changes through ICommandBus only.
NEVER mutate PlayerState outside ShowController thread.
MUST keep audio callback allocation-free and lock-free.
DO verify zero-alloc claim with allocation-counter test.

### Scenario C — Convention drift

NEVER leave comments or XML-doc in audited code.
MUST use file-scoped namespaces and immutable records.
DO enforce zero warnings under TreatWarningsAsErrors.
ALWAYS name contracts through identifiers and tests, NEVER prose.

> **HARD GATE** — Do NOT present audit verdict without files-read list, `file:line` violations, and build evidence.

→ verify: `dotnet build --no-incremental 2>&1 | tail -3`
