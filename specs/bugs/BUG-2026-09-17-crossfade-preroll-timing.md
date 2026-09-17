# BUG-2026-09-17-crossfade-preroll-timing: no smooth auto transition, mid-fade cut

## Problem

- What happens: at the end of a track there is no audible fade-out — the old track cuts abruptly, then the next track appears sharply, already partway out of its fade-in (a micro-pause/stutter between tracks). A crackle was also heard once on first cold start.
- What should happen: the old track fades out over the configured auto-crossfade window while the new track fades in, with no cut and no hole.
- How to reproduce: enable smoothing, set auto-crossfade ~800–1000ms, play MP3 tracks in project order to the boundary. Manual mid-track switches crossfade smoothly; only end-of-track transitions break.
- Security impact: NONE — local audio playback path, no exploit path identified.

## Root Cause Analysis

The auto transition is timing-fragile by construction, unlike the manual switch:

- The manual switch runs while the old track has plenty of audio left, so both fades always complete.
- The auto transition fires early on a predicted time budget (remaining time vs the crossfade window) and always issues full-window fades. When the early trigger arrives late, the old voice is retired at its natural end mid-fade, cutting it at partial volume, while the new voice is already loud — exactly the reported cut plus abrupt entry.
- Contributing factors: MP3 edge silence framed a quiet hole at every handoff (fixed separately via first-frame tag delay skip and leading-silence scan); the fade pair is now linear out with fast-attack logarithmic in.
- The cold-start crackle is a separate, unreproduced observation, likely a device-start transient; tracked as a watch item, not part of this fix.
- This is related to, not a recurrence of, the earlier crossfade curve work: same scope (audio-crossfade), new mechanism (timing, not curves).
- Risk level: Medium (touches the shared stream-open chain, but all callers keep default behavior).

## TDD Fix Plan

1. **RED**: preroll arriving with 300ms remaining against a 900ms window fades both voices 300ms.
   **GREEN**: thread the measured lead (min of window and actual remaining) from the early trigger through stream open into both fade specs.
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter PreRollLate_ClampsFadesToRemaining`
2. **RED**: (existing overlap test) preroll with 500ms remaining fades 500ms, not the full window.
   **GREEN**: update expectation as an intended consequence of the clamp.
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter PreRoll_StartsNextBeforeOldEnds_WithAudibleOverlap`
3. **RED**: leading-silence scan finds the first audible frame (untagged MP3 support).
   **GREEN**: pure scan function plus open-time skip with fresh-reopen fallback.
   **verify**: `dotnet test tests/ARIA.Audio.Tests --filter FirstAudibleFrame`

**REFACTOR**: none needed; lead is an optional parameter defaulting to previous behavior on all other paths.

## Acceptance Criteria

- [x] Late preroll completes both fades exactly at the boundary (300/300 test)
- [x] On-time preroll behavior unchanged (full window when remaining covers it)
- [x] Boundary path without early trigger unchanged
- [x] All new tests pass
- [x] Existing tests still pass (Core 266/266, App 349/349, Audio 143/143)

## Resolution

Equal-power smoothing crossfades shipped (Option A, sqrt/sqrt): fade-out forced to Logarithmic on smoothing crossfade override paths (auto AdvanceWithCrossfade, manual ReleaseOld), pairing with existing sqrt fade-in. Power sum 1.0 constant, removing the -1.25 dB construction dip of the Linear-out/sqrt-in pair. Solo Start/Stop/Seek fades and smoothing-off behavior unchanged. Tests: AutoCrossfade_UsesEqualPowerCurves + manual-path test RED then GREEN; boundary expectation updated as intended consequence. Suites: Audio 144, Core 268, App 349, all green. Temp probe files deleted.

## Follow-up (2026-09-17, wave 3): manual logic at track end

- Done literally: auto transition now runs the manual sequence (`StartFromOrder` + `ReleaseOld(manual: true)`) from `CheckPreRoll`, no lead clamp, no separate auto path.
- Two measured corrections on the way: trigger at `ManualCrossfade`, not window/2 (window/2 with a 0.4 s manual fade cuts 1.1 s of tail; trigger at manual-fade length = zero cut), and symmetric fades out+in = `ManualCrossfade` sqrt/sqrt (out 0.4 / in 3.0 gives a hole by construction: old dead, new at 0.37 gain — probed at -25.1 dB zone).
- Probe numbers (same rig, loud material): 3 s-lead overlap -1.2 dB avg; asymmetric manual -25.1 dB hole; symmetric manual -1.0 dB avg, min -19.5 single window, no hole. `AutoCrossfade` now serves only the boundary fallback swell.
- Suites: Core 270, Audio 144, App 349, all green. Temp probes deleted.

## Follow-up (2026-09-17, wave 2): envelope vs mixer separated, half-window answered

- Solo file envelope (same MP3, 0.1 s windows): 0-1 s avg -13.8 dB, 1-2 s avg -16.1 dB, 2-3.4 s avg -20.7 dB min -22.7 dB (natural quiet intro passage), 7-10 s avg -15.6 dB min -19.9 dB single window, 6-9.5 s avg -15.5 dB. The -21..-22 dB shelf exists only at file 2.5-3.4 s, nowhere near the fade region.
- Transition probe (cue-out 10 s, 3 s window, same file both tracks): pre-fix zoneMin -21 dB vs edgeAvg -16 dB (~5 dB); post-fix rerun zoneMin -21.2 dB vs edgeAvg -16.1 dB (switch 7430 ms) — unchanged within probe noise. Verdict: construction dip (-1.25 dB) fixed by sqrt/sqrt, but this probe is confounded by track B starting at file 0 (its quiet 2-3.4 s passage falls in the overlap tail) plus unequal source levels, so the probe cannot validate audibility. Honest status: math fixed and tested; audibility needs production timeline or loudness-matched material, not this probe.
- Half-window proposal: superseded by wave 3 — auto transition now IS the manual sequence (trigger at `ManualCrossfade`, symmetric sqrt/sqrt fades). Async pre-open unchanged; no-fade part never adopted (fade-free boundary fallback is the defect path, removing fades re-exposes MP3 edge silence).

## Diagnosis log (diagnose-root)

- Reproduce: consistent in production (MP3, wired output, smoothing on, auto window up to 3s); manual mid-track switches stay smooth. Not reproduced in paced realtime probes with synthetic tones, WAV files, or a real MP3 through the full production-like stack (pumped bus, marshal, monitor, mixer): overlap envelope clean, open latency milliseconds, imported durations decode-exact.
- Isolate: auto-transition timing path (early trigger on remaining time vs window, async next-stream open, retire-on-end). Manual path needs no prediction and always overlaps; boundary path without early trigger hard-cuts the old voice and swells the new one from silence.
- Hypothesize (ranked): early trigger never fires in production (position snapshots stall or mismatch) leaving only the boundary path; trigger fires late so the old voice is retired mid-fade; next-stream open is slow (MP3 decode, silence scan, seek, marshal, pump backlog) leaving a gap the fade-in cannot cover.
- Verify: pending production timeline (early-trigger fire with remaining/lead values, fade durations issued, open latency, end-event arrival order). Temporary env-gated timeline log added for one instrumented run, to be removed after.

## Follow-up (2026-09-17): transition DSP verified clean

- A paced realtime probe (production-like bus, marshal, monitor, mixer) with two different tones shows a clean swell (0.50 → 0.60 → 0.50), no hole.
- A same-tone probe shows a deep dip, but the math proves it is phase cancellation between the two identical tones (a ~1ms start offset lands near opposite phase at 440Hz), not a product defect. Uncorrelated material cannot cancel this way.
- Real-file open latency measured in milliseconds; imported durations decode-exact.
- Side finding (minor, untouched): initial Play ramps the first track over the full auto window instead of the short start fade. Audibly a gentle swell-in, not a defect in transitions.
- Remaining suspects are environmental (GC/device dropout) or file-specific; next step is on-device transition timing diagnostics, not more fade changes.
