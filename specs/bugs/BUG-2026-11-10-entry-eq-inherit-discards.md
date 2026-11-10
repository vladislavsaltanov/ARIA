# BUG-2026-11-10T104600: per-entry эквалайзер молча отбрасывает правки при «Наследовать настройки трека»

## Problem

Actual: в диалоге «Звук вхождения» ползунки эквалайзера (и уровня/баланса) при включённом «Наследовать настройки трека» двигаются, но настройки не сохраняются и не применяются к звуку; при повторном открытии диалога ползунки сброшены.

Expected: изменение любого параметра сохраняется как override вхождения и применяется к звуку.

Security impact: NONE.

## Root Cause Analysis

`CreateEntryAudioEditor` ставит `InheritTrackSettings = entry.Overrides?.Audio is null` — для вхождения без override это `true`. Все сеттеры параметров (`GainDb`, `Pan`, `EqBands[*].GainDb`, `NormalizeEnabled`, `TargetLufs`, `OwnTargetLufs`) идут через `SubmitCurrent`, который при `_inheritTrackSettings == true` отправляет `SetEntryAudio(entry, null)` — то есть отбрасывает изменение. UI не получает подтверждения от модели, ползунок остаётся сдвинутым до переоткрытия диалога; `CurrentEffectiveAudio` (ShowController) продолжает отдавать `track.Defaults.Audio`, поэтому звук не меняется.

Глобальный эквалайзер в настройках проверен и не затронут: `SetGlobalAudio` → `ShowController._globalAudio` → `engine.SetGlobalAudio` → `ApplyMasterChain` → `_masterEq`; `MixerDelta` триггерит автосейв, `RestoreShow`/`SetGlobalAudio` восстанавливает значение.

Risk level: Low. Поведение детерминированное.

## TDD Fix Plan

1. **RED**: `TrackAudio_EntryEditWhileInheriting_CreatesOverride` — `entryId` + `InheritTrackSettings = true`, очистить submitted, `EqBands[3].GainDb = 5`; последняя команда — `SetEntryAudio` с не-null `Audio`, band gain 5, `InheritTrackSettings == false`.
   **GREEN**: в `SubmitCurrent` при `_inheritTrackSettings == true` молча снять флаг (поднять `PropertyChanged`) и отправить `Current()`; сеттер `InheritTrackSettings` должен сам отправлять `null` (true) / `Current()` (false), не проходя через auto-opt-out.
   **verify**: `dotnet test tests/ARIA.App.Tests --filter TrackAudio_EntryEditWhileInheriting`

2. **RED**: `TrackAudio_EntryInheritToggleOff_SubmitsCurrentAudio` — после `InheritTrackSettings = true` (null) и `= false` — `SetEntryAudio` с текущими значениями.
   **GREEN**: тот же фикс.
   **verify**: `dotnet test tests/ARIA.App.Tests --filter TrackAudio_EntryInheritToggleOff`

## Acceptance Criteria

- [ ] Правка эквалайзера/уровня/баланса в entry-редакторе сохраняется даже если «Наследовать» был включён
- [ ] Чекбокс автоматически снимается при первой правке
- [ ] Явное включение «Наследовать» очищает override (существующий тест `TrackAudio_EntryInherit_SubmitsNull` зелёный)
- [ ] Существующие тесты VM зелёные
- [ ] 0 предупреждений

## Resolution

Fixed 2026-11-10, branch `fix/import-eq-bugs`, commit `ae99a83`.

`TrackAudioVm.SubmitCurrent` при `_inheritTrackSettings == true` снимает флаг (с `PropertyChanged`) и сохраняет `Current()`; сеттер `InheritTrackSettings` отправляет `null` (true) / `Current()` (false) напрямую, минуя auto-opt-out.

Тесты: `TrackAudio_EntryEditWhileInheriting_CreatesOverride`, `TrackAudio_EntryInheritToggleOff_SubmitsCurrentAudio`.

Проверено: `dotnet test ARIA.slnx` — 1318 тестов зелёных, 0 предупреждений.

Дополнительно 2026-11-10, коммит `222d574`: диалог при мгновенном переоткрытии строился из отстающей строки VM и показывал старые значения («сброс сразу после закрытия»), а правка из него затирала свежесохранённое; `SetEntryEndAction` из stale-строки мог затереть свежий Audio. Фикс: `CreateEntryAudioEditor` и `SetEntryEndAction` читают overrides из `bus.Snapshot()`. Тест `EntryEditorFreshnessHeadlessTests.ImmediateReopen` (RED→GREEN).

Дополнительно 2026-11-10, коммит `b64d8a9`: кнопка «Прослушать» в диалоге вхождения играла `EffectiveSettings.ForTrack` и игнорировала override — казалось, что эквалайзер «не применяется». `StartPreviewTrack` получил опциональный `EntryId` (wire-совместимо), контроллер резолвит entry, `TrackAudioVm.Preview` передаёт `_entryId`. Проверено: `dotnet test ARIA.slnx` — 1322 зелёных, 0 предупреждений.
