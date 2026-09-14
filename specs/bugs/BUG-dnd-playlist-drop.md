# BUG: OS file drop into playlist does nothing

## Phase 1 — Reproduce

Reported: dragging a track from Explorer/Finder into the playlist does nothing.

Environment: macOS 26 (Tahoe), Avalonia 12.1.2, .NET 10 SDK 10.0.401, branch batch2-transport-ui.

Code path: `PlaylistCenter.axaml` (`DragDrop.AllowDrop="True" DragDrop.Drop="OnFilesDropped"`
on `EntryList`) → `PlaylistCenter.axaml.cs OnFilesDropped` → `TryGetFiles` → `TryGetLocalPath`
→ `PlaylistsViewModel.ImportAudioFilesAsync`.

Established fact: existing `PlaylistAudioImportTests` calls `ImportAudioFilesAsync` directly.
The real `DragDrop.Drop` event path (routing → handler → `TryGetFiles` → `AudioImport`
fallback via `App.Host`) was NEVER executed by any test. Not a regression — untested path.

Failure surface is all-silent: `TryGetFiles` null → bare return; no playlist → status line;
`AudioImport` null → status line. No dialog, no crash.

Repro strategy: headless test raising the real `Drop` event with a file payload through a
real window; observe whether an entry appears.

## Phase 2 — Isolate

Headless repro (`OsDropReproTests.DropEvent_WithFilePayload_ImportsEntry`): raising the real
`Drop` event with a real `IStorageFile` payload imports NOTHING — entries=0, tracks=0 after 10s,
status frozen at progress text `импорт: os-drop.wav`.

Narrowed down the stall point:

- Event routing works: handler runs, `TryGetFiles`/`TryGetLocalPath` return the file,
  `SelectedPlaylist` set, `AudioImport` set, `_audioImport` invoked (progress callback fired).
- `await _audioImport(...)` never completes. `ImportTracksAsync` itself is proven fine —
  the same call from the test thread completes in ms (existing `PlaylistAudioImportTests`,
  plus direct-call experiment).
- No swallowed exceptions: `UnobservedTaskException` + `AppDomain.UnhandledException` traps
  stayed empty.
- No locks in `TrackImporter`/`MiniaudioSourceFactory`/`WaveformScanner`; SQLite store opens
  a fresh connection per operation (no thread affinity).
- Decisive probe (`UiPumpProbeTests`, since removed): an `async void` UI chain awaiting
  `Task.Delay(200)` NEVER resumes in headless — neither without pumping nor across repeated
  `Dispatch` pumps. UI continuations posted outside `Dispatch` are stalled by the harness.
- `OnFilesDropped` is `async void` on the UI thread: `await Task.Run(...)` completes on the
  pool, but the resume is posted to the stalled UI context. Harness artifact, not product code.
- Once the shared import pipeline is primed by one direct call (test thread, no UI context),
  the FULL drop chain completes end-to-end in ~470ms: Drop event → handler → import →
  match → `AddEntry` → entry visible. Proven by the same test with a warmup import.

Conclusion: the product chain (event → handler → import → match → entry) is SOUND. The stall
lives in the headless harness, which cannot reproduce the OS-to-app event boundary anyway.
The production failure must be at that boundary or in the user's context.

## Phase 3 — Hypothesize (ranked, production)

- H1: `Drop` never reaches `EntryList` in the real app (routing, `DragOver` effects on macOS,
  drop target obscured, window focus). Falsify by user observation: status line unchanged +
  button works.
- H3: silent early return in the user's context — no playlist selected (`нет плейлиста`), or
  stale build predating the feature. Falsify by user observation of the status line + build date.
- H2 (`TryGetFiles` null for real payloads) demoted: identical pattern served the deleted
  LibrarySection, and harness proves handler+payload parsing works with a real storage file.

## Phase 4 — Verify

Pending user evidence (asked 2026-09-13): status line content after drop, does the
`В плейлист` button import the same file, where exactly the file is dropped.

Evidence received 2026-09-13:

- Status shows import progress after drop → H1 refined: the `Drop` event DOES reach
  `EntryList` and the handler runs (progress text can only come from this flow).
- The `В плейлист` button imports the same file → import pipeline, matching and `AddEntry`
  all work; product chain proven in production too.
- Remaining: entry never becomes visible. Code-level suspects narrowed to two:
  (a) match fails in the drop context (`файлы не распознаны`) — but `ExpandAudioFiles`
  requires `File.Exists`, and progress fired, so the dropped string is a real file;
  exact-match should then hit; (b) entry added but hidden by `CenterSearchText` filter
  (`RefreshVisible` skips non-matching rows) — user would see progress flash, count+1,
  no new row. Next discriminator: final status text + search box content + counter change.

Evidence received 2026-09-13 (decisive):

- Final status: `в плейлист добавлено: 1` → import, match and `AddEntry` ALL succeed.
- Search box empty → filter not hiding.
- Counter grows → entry is in `SelectedPlaylist.Entries`.
- So the row IS in state but the user never sees it.
- Code facts: `BuildKey` includes entries → no early-return; `Rebuild` always runs
  `RefreshVisible` + `RefreshCenterHeader` together (counter update proves full refresh ran,
  so `VisibleEntries` DOES contain the new row). `AddEntry` with null index appends at END.
  Zero scroll-to-new calls exist in `PlaylistCenter`/`PlaylistsViewModel` (grep empty).
- Single remaining root-cause candidate: new row appended below the fold, never revealed.
  Pending final user confirmation (is the track at the bottom after scrolling down?).

Root cause CONFIRMED 2026-09-13: user scrolled down — the dropped track IS at the bottom.
New entries append at end (`AddEntry` null index) with zero reveal: no scroll-to-new call
existed anywhere in `PlaylistCenter`/`PlaylistsViewModel` (grep empty). Import "worked"
(status, counter) while the row stayed below the fold — perceived as total failure.

Fix: reveal-on-import. `PlaylistsViewModel` snapshots reveal targets before submitting
`AddEntry` (before, not after — Inline bus rebuilds synchronously inside `Submit`, proven
by a failing test on the naive order), resolves the new row at the end of `Rebuild`, selects
it and raises `RevealRequested`; `PlaylistCenter` scrolls it into view. Covers both drop
and `В плейлист` button (same method). Mismatch/playlist-switch clears silently.
Tests: `ImportAudioFilesAsync_RevealsAddedEntry` (event + selection + visibility, sync Inline
bus) and end-to-end `OsDropReproTests` (Drop event → entry). Full suite green.

## Phase 5 — Closed

Status: CLOSED 2026-09-14. User confirmed reveal works in the live app (list
scrolls to the new row, row selected). Follow-up polish shipped on top:
success status auto-dismisses after 10s (`transientStatusTtl`), unmatched files
raise an `AudioImportIncomplete` modal listing names (partial and total mismatch).
Tests: mismatch event, partial counts, TTL auto-clear. Suite green.
