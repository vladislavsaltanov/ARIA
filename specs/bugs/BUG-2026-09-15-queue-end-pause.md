# BUG-2026-09-15T131800: end-of-track ignores queue for Pause/Stop/Replay; no visible checkmark in After menu

## Problem

Actual:

- Последний entry плейлиста с EndAction Pause (override, track default или global): по окончании трека транспорт встаёт на паузу, даже когда в очереди есть треки.
- То же для Stop (стоп вместо перехода к очереди) и Replay (бесконечный повтор морит очередь голодом).
- В контекстном меню «Действие в конце» текущий режим никак не виден: галочка не отображается, хотя логика IsChecked есть.

Expected:

- Очередь — явное намерение оператора «играть следующим»: непустая очередь побеждает Pause/Stop/Replay, трек из очереди стартует автоматически.
- Pause/Stop/Replay срабатывают только когда очередь пуста.
- В подменю «Действие в конце» текущий режим помечен видимой галочкой; без override помечен «Наследовать».

Repro (core):

- Playlist [t1(Pause)], очередь [t2]. Play → End(t1, Completed) → сейчас Paused на t1, надо Playing на t2.

Repro (UI):

- Открыть контекстное меню entry → «Действие в конце»: ни один пункт визуально не отмечен.

Security impact: NONE — no trust boundary, no exploit path identified.

## Root Cause Analysis

Путь: конец стрима → HandleCurrentEvent → ApplyEndAction (switch по Settings.EndAction текущего entry).
Очередь проверяется только на ветке Advance (AdvanceFromBoundary → StartFromOrder, queue-first).
Ветки Pause/Stop/Replay очередь не смотрят: ставят Paused/Stopped/restart и возвращаются.
Контракт Queue («сначала Queue, затем Playlist») нарушен для всех не-Advance режимов.

UI: OnEndActionMenuOpened выставляет IsChecked корректно (покрыто тестом), но пункты подменю в axaml без ToggleType — Avalonia не рисует checkmark без него.

Контрибьюторы: EffectiveSettings отдаёт Pause как валидное значение для последнего entry; ручной Play с границы уже идёт через AdvanceFromBoundary (queue-first) — то есть ручное поведение правильное, чинить только авто-переход.

Risk level: Low. Один прод-вызыватель Core (AppHost через ICommandBus), поведение меняется только в комбинации «не-Advance EndAction + непустая очередь», ранее не покрытой тестами.

## TDD Fix Plan

1. **RED**: Core-тест `EndAction_Pause_WithQueuedItem_AdvancesToQueue`: playlist [t1(Pause)], EnqueueTrack(t2), Play, End(t1) → Playing, Current=t2, очередь пуста.
   **GREEN**: В авто-переходе конца трека проверять очередь первой: непустая очередь → старт из очереди независимо от EndAction.
   **verify**: `dotnet test tests/ARIA.Core.Tests`

2. **RED**: Core-тесты аналогично для Stop и Replay с непустой очередью.
   **GREEN**: Тот же путь покрывает все три (одна проверка очереди поверх switch).
   **verify**: `dotnet test tests/ARIA.Core.Tests`

3. **RED**: UI-тест: открыть «Действие в конце» для entry без override → пункт Наследовать IsChecked и имеет ToggleType для видимой галочки; для entry с override Pause → отмечен Пауза.
   **GREEN**: axaml: ToggleType (+GroupName) на 5 пунктах подменю. IsChecked-логика уже есть, не трогать.
   **verify**: `dotnet test tests/ARIA.App.Tests`

**REFACTOR**: нет — изменение в 2 точках (Core-переход, axaml-разметка).

## Acceptance Criteria

- [ ] Pause/Stop/Replay + непустая очередь → автостарт первого из очереди, статус Playing
- [ ] Pause/Stop/Replay + пустая очередь → старое поведение без изменений
- [ ] Advance + очередь/плейлист → без изменений (регресс-тесты зелёные)
- [ ] Подменю показывает галочку текущего режима; по дефолту — Наследовать
- [ ] `dotnet test` на всём солюшене зелёный, 0 warnings
- [ ] Без комментариев в коде

## Resolution

<!-- filled in by validate-fix -->
