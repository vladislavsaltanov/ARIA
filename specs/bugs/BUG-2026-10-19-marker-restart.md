# BUG-2026-10-19-marker-restart: достижение метки перезапускает трек, нет per-marker действия

## Problem

- What happens: достижение любой метки во время воспроизведения перезапускает трек с начала (при entry EndAction = Replay) либо уводит дальше по очереди/в стоп по entry EndAction. Собственного действия у метки нет.
- What should happen: у каждой метки переопределяемое действие: ничего (проезд мимо), пауза на метке, повтор от прошлой метки (loop), повтор от начала трека (loop). Стопа нет (подтверждено 2026-10-19).
- How to reproduce: трек с меткой, entry EndAction = Replay (или глобальный default Replay), Play, дождаться метки — трек начинается сначала вместо действия метки.
- Security impact: NONE — локальный playback-путь, exploit path отсутствует.

## Root Cause Analysis

Using systematic-debugging (reproduce → isolate → hypothesize → verify):

- Reproduce: Play → движок armed markers (`ArmedMarkers(deck)`), MixerBus останавливает голос на первой метке `> blockStart`, событие `StoppedByMarker(MarkerPosition)`.
- Isolate: `ShowController.HandleCurrentEvent`: `StoppedByMarker` складывается в один `switch` с `Completed`/`CueOutReached` → `ApplyEndAction()` читает только `Settings.EndAction` вхождения. Поле `Marker.Action` в решение не входит вообще. `MarkerAction` сегодня одно значение — `Stop`, используется лишь как заглушка данных.
- Hypothesize: корень один — диспетчер конца стрима не резолвит действие метки по `e.MarkerPosition`; entry EndAction применяется к чужой границе. Ранжирование: (1) отсутствие dispatch — подтверждается кодом; (2) движок виноват — отвергнуто, движок корректно отдаёт позицию; (3) floor виноват — отвергнуто, floor лишь перевооружает.
- Verify: `PlaybackTests.MarkerStop_TriggersEndAction` прямо фиксирует текущее поведение — метка триггерит entry EndAction (advance на t2). Повторяется детерминированно в Core-тестах, не флейк.
- Risk level: Medium — трогает общую цепочку конца стрима (`HandleCurrentEvent`, `OnPlay` Stopped-ветка), но маркерная ветка отделяется от `Completed`/`CueOutReached`, их поведение не меняется.

Связь с историей: не рецидив, новых багов по меткам в registry нет. Ближайший контекст — `specs/track-end-action.md` (EndAction применяется в т.ч. к автостопу по Marker — этот контракт меняется, см. MODIFIED ниже).

## Decision points (brainstorming, 2 варианта каждый)

1. `None` = проезд мимо. Подтверждено пользователем 2026-10-19. Вооружение = фильтр `m.Action != None`.
2. Повторы loop. Подтверждено 2026-10-19, консистентно с `Replay`. Выход: Next / Stop / ручной seek.
3. Resume после marker Pause: переиспользовать `MarkerFloor` vs отдельное поле. Рекомендовано: отдельное `ResumeAt` на deck (floor — только фильтр вооружения; `Completed` сбрасывает floor в Zero, маркерная пауза — нет).

## TDD Fix Plan

Контракт модели:

```csharp
public enum MarkerAction { Pause, None, RepeatFromPrevious, Restart }
```

`Pause = 0` — старый `Stop` тоже был 0, legacy-данные молча становятся паузой (смысл сохранён: остановка на метке). Mappers/Remote codec кастуют int, формат не меняется.

Поведение диспетчера `StoppedByMarker` (entry EndAction на маркерной границе больше не читается):

- None: не взводится вообще (фильтр `ArmedMarkers`). Defensive fallback при `StoppedByMarker` без метки в списке — трактовать как Pause.
- Pause: floor=fired, `ResumeAt`=fired, хэндл освобождён, статус Paused, `_atEndBoundary=false`. Play продолжает с `ResumeAt` тем же треком.
- RepeatFromPrevious: target = предыдущая метка `< fired`, иначе CueIn. Seek на target, floor=target (loop), играть.
- Restart: seek на CueIn, floor=CueIn (loop), играть.
- `OnPlay`: Paused-ветка — хэндл есть → resume; хэндла нет + `ResumeAt` → старт потока с `ResumeAt`. Stopped-ветка — `ResumeAt` приоритетнее CueIn. `Completed`/`CueOutReached` сбрасывают floor=Zero и чистят `ResumeAt`.

1. **RED**: `ParseMarkerAction`/`ToMarkers` roundtrip 4 значений; `(MarkerAction)0 == Pause`; legacy-пейлоад со Stop парсится как Pause.
   **GREEN**: переименовать `Stop`→`Pause`, добавить `None/RepeatFromPrevious/Restart` + все `MarkerAction.Stop` использования по src/tests; `PlaylistFormat`/`Mappers`/`CommandCodec` без смены формата (int-каст).
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter PlaylistFormatMarker`
2. **RED**: `ArmedMarkers` исключает `None` (2 метки → взведена 1).
   **GREEN**: фильтр в `ArmedMarkers`.
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter MarkerFloor`
3. **RED**: marker Pause — статус Paused, Play продолжает с позиции метки, трек тот же.
   **GREEN**: ветка Pause в маркерном диспетчере + `ResumeAt` + OnPlay-resume (старт потока с `ResumeAt` при Paused без хэндла).
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter MarkerPause`
4. **RED**: RepeatFromPrevious — seek на прошлую метку, loop (второе достижение той же метки снова прыгает назад); одиночная метка → прыжок на CueIn.
   **GREEN**: ветка repeat + `JumpDeckTo` на target.
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter MarkerRepeat`
5. **RED**: Restart — seek на CueIn, дальнейшее достижение метки снова рестартует (loop).
   **GREEN**: ветка restart.
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter MarkerRestart`
6. **RED**: обновление `MarkerStop_TriggersEndAction` → `MarkerPause_HoldsCurrentTrack` + переименование `MarkerAction.Stop` → Pause по src/tests (MODIFIED: метка больше не advance'ит).
   **GREEN**: ожидание Paused + тот же трек; `PlaceMarker`/BeginMarker default None.
   **verify**: `dotnet test tests/ARIA.Core.Tests --filter MarkerStop_TriggersEndAction`
7. **RED**: UI — ховер 500мс над меткой открывает попап выбора действия, 4 русских лейбла «Проезд / Пауза / Повтор от прошлой / С начала» (context7: `Enum.GetValues<T>()` + `SelectedItem` + record-обёртка с `ItemTemplate` для лейблов, `/avaloniaui/avalonia-docs` ComboBox how-to).
   **GREEN**: `TransportViewModel.SetMarkerAction` + ховер-попап в `PlaybackHeader` (`DispatcherTimer` 500мс, закрытие на смене ховера/exit/прессе) + хинт.
   **verify**: `dotnet test tests/ARIA.App.Tests --filter Marker`
8. **RED**: remote roundtrip action int ↔ enum.
   **GREEN**: правки кодека при необходимости (каст уже generic).
   **verify**: `dotnet test tests/ARIA.Remote.Tests --filter Marker`

**REFACTOR**: вынести маркерный dispatch из `HandleCurrentEvent` в `ApplyMarkerAction(Marker)`; `ArmedMarkers` без аллокаций сверх существующего паттерна (аудио-callback не тронут — движок не меняется).

## Acceptance Criteria

- [x] Метка с None проезжается без остановки и без события
- [x] Пауза останавливается на метке и продолжает с неё по Play
- [x] RepeatFromPrevious loops сегмент; Restart loops от CueIn
- [x] Entry EndAction применяется только на Completed/CueOutReached, не на метках
- [x] Legacy-данные (Stop=0) открываются как Pause; legacy `"stop"` в project-файлах парсится в Pause
- [x] UI меняет действие метки (ховер-попап 500мс); remote roundtrip цел (int-каст без смены формата)
- [x] `dotnet test` весь солюшен зелёный, 0 warnings (Core 352, Persistence 49, App 420, Audio 190, Remote 116)

## Resolution

Shipped: `MarkerAction` = Pause(0)/None/RepeatFromPrevious/Restart; legacy `Stop` переименован в `Pause`, значение 0 сохранено. `StoppedByMarker` отделён от `Completed`/`CueOutReached` → `ApplyMarkerAction` (fallback Pause); None фильтруется в `ArmedMarkers` (движок не тронут). Пауза: floor+`ResumeAt`=fired, Play продолжает с метки (`JumpDeckTo`); seek/стоп/реплей чистят `ResumeAt`. UI: ховер 500мс → попап 4 действий, `SetMarkerAction` в VM, хинт дополнен. Tests: `MarkerActionTests` (5), `MarkerFloor_SkipsFiredMarkerOnResume`, `MarkerPause_HoldsCurrentTrack`, `Import_LegacyStopAction_MapsToPause`, `SetMarkerAction_UpdatesMarkerThroughBus`; Core 352 зелёных. Попап не покрыт headless-тестом (таймер) — ручная проверка: навести, подождать, выбрать.

## Follow-up 2026-10-19: popup flicker (overlay under cursor)

Symptom: hover-popup opened over the marker, the cursor ended up over the popup,
WaveformBorder fired PointerExited, popup closed instantly.

Fix: popup anchored below the tape (WaveformStrip bottom-left, gravity bottom-right),
no IsLightDismiss; close only via x-button, action click, press, or 150ms grace timer
that starts on border/popup PointerExited and cancels on popup PointerEntered or
re-hover. Gesture hint moved from border tooltip into popup footer.
RED: `MarkerActionPopupHeadlessTests.HoverThenBorderExit_PopupStaysOpenDuringGrace`
failed on 446b58b (closed synchronously), green after fix.
