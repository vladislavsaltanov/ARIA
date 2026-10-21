# BUG-2026-10-21-normalize-blocks-control: замер LUFS замораживает управление шоу

## Problem

- Actual: `NormalizeTrack`/`NormalizeProject` декодируют файлы целиком (`ScanTrackLufs`) прямо на Control-потоке. Нормализация плейлиста из 30 треков занимает десятки секунд. Всё это время PANIC, Stop, Next, EndAction и автопереходы стоят в очереди.
- Expected: скан идёт в фоне; управление отвечает мгновенно; результат применяется на Control-потоке и только если трек и путь не изменились.

## Root Cause Analysis

- Для открытия стримов асинхронный паттерн уже был (`StartStreamFor`: `Task.Run` + marshal), для скана LUFS — нет.
- Дополнительно: PANIC стоял в общей FIFO-очереди и ждал весь накопленный бэклог.
- Risk level: Critical во время шоу.

## Fix

- `ScanLufsThen`: скан в `Task.Run`, применение через marshal. Без marshal (Inline-тесты) выполняется синхронно, как раньше. Применение заново ищет трек и сверяет `FilePath`; исчезнувший трек отклоняется как `unknown-track`.
- `CommandBus`: PANIC идёт приоритетной полосой и обгоняет бэклог очереди.
- `CommandBus`: насос — выделенный поток `aria-control`, а не продолжения на тред-пуле: голодание пула (импорт, сканы, HTTP) больше не задерживает управление. Найдено флаком в полном прогоне.

## Acceptance Criteria

- [x] Медленный скан не блокирует `SetMasterGain` (`NormalizeOffControlThreadTests.NormalizeTrack_SlowScan_KeepsControlThreadResponsive`)
- [x] Медленный скан проекта не блокирует PANIC (`NormalizeProject_SlowScan_KeepsPanicResponsive`)
- [x] Трек, исчезнувший за время скана, не воскресает (`NormalizeTrack_TrackRemovedDuringScan_RejectsInsteadOfResurrecting`)
- [x] PANIC обгоняет очередь (`BusResilienceTests.Panic_OvertakesQueuedBacklog`)
