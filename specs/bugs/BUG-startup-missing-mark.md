# BUG-2026-09-15T000000: missing-file badge appears only after playback attempt

## Problem

Actual: the unavailability badge lights up only when the user tries to play the track.
Expected: missing files are flagged at app/playlist load, before any playback attempt.

Security impact: NONE. Local file-existence scan plus local state flag.

## Root Cause Analysis

Verified in code: the fault set is populated exclusively from engine stream events, so a track whose file vanished while the app was closed stays unflagged until the engine fails to open it. No load-time check exists anywhere.

Fix shape: IO stays at the app boundary. The host scans library file existence once at startup and submits a command carrying the missing ids; the controller flags known ids and emits a transport delta so existing viewmodel rendering lights the badge with zero view changes. Unknown ids are ignored. Snapshot-only tracks without library rows are out of scope: snapshots are always written from library tracks, so the library is the complete source in practice. Risk: Low.

## TDD Fix Plan

1. **RED**: core test — load show, submit missing ids (one known, one unknown), known id flagged, no playback involved.
   **GREEN**: new command plus handler that flags known ids and emits transport on change.
   **verify**: dotnet test tests/ARIA.Core.Tests

2. **RED**: host test — seed library plus snapshot with a ghost file, start, transport faulted contains the id.
   **GREEN**: startup scan in the host after name repair.
   **verify**: dotnet test tests/ARIA.App.Tests

**REFACTOR**: none.

## Acceptance Criteria

- [x] Missing files flagged at startup, badge visible before any play attempt
- [x] Unknown ids never pollute the fault set
- [x] All new tests pass
- [x] Existing tests still pass

## Resolution

Fixed 2026-09-15. New MarkMissing command flags known ids and emits
transport on change; the host scans library file existence once at
startup after name repair. Existing viewmodel rendering lights the
badge with zero view changes.
Tests: Core 178, App 251, Persistence 21, Audio 70, Remote 59.

Follow-up 2026-09-15: badge still appeared only after a play attempt.
Root cause: the viewmodel is constructed after startup, and its fault
mirror filled only from live transport deltas, missing the one emitted
at startup. Fix: seed the mirror from the bus snapshot in the
constructor. Tests: Core 178, App 252, Persistence 21, Audio 70,
Remote 59.

Status: CLOSED 2026-09-15.
