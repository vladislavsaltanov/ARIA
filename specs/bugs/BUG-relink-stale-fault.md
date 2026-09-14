# BUG-2026-09-14T231500: relink keeps fault icon lit, no import-time missing-files dialog

## Problem

Actual:

- "Найти…" picks a replacement file, the track is rewritten in place, but the fault icon stays lit and no success feedback appears. Looks like nothing happened.
- Playlist import with unavailable files shows only a status-line line. No modal appears at import time.

Expected:

- After successful relink the fault mark clears immediately with visible confirmation.
- Import with missing files pops one aggregated modal right away, even when many files are missing.

Security impact: NONE. No trust boundary crossed; only local file paths shown in a local dialog.

## Root Cause Analysis

Verified in code, two independent causes:

1. Stale fault mirror. The controller clears its own fault set when a same-id track replacement arrives, but emits only a show delta, not a transport delta. The viewmodel mirror of the fault set updates exclusively on transport deltas, so it keeps the stale id and the row keeps rendering the alert icon. The data was replaced; only the notification was missing. Risk: Low.

2. Missing import-time signal. The import path returns a report containing the missing-file list but raises no event for it, so the view has nothing to open a dialog from. Additionally the freshly imported playlist is not auto-selected, so a bulk "find files" repair would append to the wrong playlist. Risk: Low.

Contributing factor: OS file-drop onto the playlist is a separate known untested path (see BUG-dnd-playlist-drop). When relink appeared to do nothing, manual drop was the fallback and also silently did nothing, compounding the "broken" impression. Out of scope here.

Not a regression. Missing feature + missing notification.

## TDD Fix Plan

1. **RED**: viewmodel test — faulted row, then same-id track replacement submitted, row is no longer faulted.
   **GREEN**: controller emits a transport delta when a replacement clears a fault.
   **verify**: dotnet test tests/ARIA.Core.Tests && dotnet test tests/ARIA.App.Tests

2. **RED**: viewmodel test — document import with unknown files raises a missing-files event carrying the list.
   **GREEN**: raise the event from the document-import path; auto-select the imported playlist by awaited name so later appends land there.
   **verify**: dotnet test tests/ARIA.App.Tests

3. **RED**: viewmodel test — successful relink writes a transient "replaced" status.
   **GREEN**: set transient status on relink success.
   **verify**: dotnet test tests/ARIA.App.Tests

4. View: one aggregated import-missing dialog (capped list with overflow line, scrollable), buttons find-files (multi-pick audio, append via existing audio-import path) and dismiss. No new viewmodel API beyond the event.

**REFACTOR**: none expected; dialog follows the existing info-dialog shape.

## Acceptance Criteria

- [ ] Fault icon clears right after successful "Найти…" plus confirmation status
- [ ] Import with missing files opens exactly one dialog immediately, capped list for many files
- [ ] Bulk find appends into the just-imported playlist
- [ ] All new tests pass
- [ ] Existing tests still pass

## Resolution

Fixed 2026-09-14. Controller emits a transport delta when a same-id
replacement clears a fault, so the row icon clears immediately.
Document import raises a missing-files event and auto-selects the
imported playlist; the view opens one capped aggregated dialog with
bulk find (multi-pick audio appended to the fresh playlist).
Successful relink writes a transient replaced status.
Tests: Core 177, App 249, Persistence 21, Audio 70, Remote 59.
