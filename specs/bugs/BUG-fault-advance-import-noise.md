# BUG-2026-09-14T233000: fault advances to next track, find-from-import shows import progress

## Problem

Actual:

- Trying to play a broken track marks the fault icon but starts the next track.
- "Найти файлы…" from the import-missing dialog runs the full import ceremony: progress strip plus added-count status, same as drag-and-drop import.

Expected:

- Faulted track: nothing plays. Transport stops, fault is marked, no advance.
- Find-from-import: picked files land in the playlist silently, no progress strip, no status noise. Genuine decode failures still surface.

Security impact: NONE. Local playback state machine plus local dialog flow only.

## Root Cause Analysis

Verified in code, two independent causes:

1. Advance-on-fault. The stream-event handler treats a faulted current stream as an end-of-track: it disposes the handle and starts the next entry in order. An explicitly tested behavior, but wrong for missing files: the user asked to hear one track, the engine cannot open it, the controller silently substitutes another. Risk: Medium (changes long-tested playback behavior; queue/end-action paths untouched).

2. Shared noisy import path. The missing-dialog repair reuses the audio-import routine, which unconditionally writes per-file progress and a transient added-count status. That ceremony belongs to the explicit import button and drop flows, not to an in-place repair. Risk: Low.

Not a regression. Behavior change request plus noise leak.

## TDD Fix Plan

1. **RED**: rewrite the fault-advance test to stop semantics: one created stream, current null, status stopped, fault marked.
   **GREEN**: fault branch marks, disposes, stops, emits. No order start.
   **verify**: dotnet test tests/ARIA.Core.Tests

2. **RED**: silent audio-import test: stubbed importer, silent flag, progress never fires, status stays empty on success.
   **GREEN**: silent flag on the audio-import routine; missing-dialog repair passes it; failure dialog for undecodable picks stays.
   **verify**: dotnet test tests/ARIA.App.Tests

**REFACTOR**: none.

## Acceptance Criteria

- [ ] Playing a broken track stops transport, marks fault, never starts the next entry
- [ ] Find-from-import adds picks with no progress strip and no status noise
- [ ] Undecodable picks still report a failure dialog
- [ ] All new tests pass
- [ ] Existing tests still pass

## Resolution

Fixed 2026-09-14. Fault branch marks, disposes, stops, emits — no
order start; stale advance tests rewritten to stop semantics at core
and engine-integration levels. Audio import gained a silent flag
(no progress, no success status); the import-missing repair passes
it while undecodable picks still raise the failure dialog.
Tests: Core 177, App 250, Persistence 21, Audio 70, Remote 59.

Follow-up 2026-09-15: stuck strip after repair. Root cause: the
import summary was the only sticky status; silent repair never
refreshes it, and it resurfaces under cleared transient notices.
Fix: success summary moved into the document-import path as a
transient notice like all others; error paths stay sticky.
Timer mechanics proven with a posted-context test.
Tests: Core 178, App 255, Persistence 21, Audio 70, Remote 59.

Follow-up 2026-09-15: drag visuals stuck after replace. Modals
(fault dialog, file picker) tear the pointer flow, stranding the row
reorder coordinator mid-drag with insertion highlight on. Fix:
public coordinator reset, called on both dialogs opening and after
relink, missing-import and OS-drop completions. Headless test proves
interrupted drag leaves no classes and no spurious move.
Tests: Core 178, App 256, Persistence 21, Audio 70, Remote 59.
