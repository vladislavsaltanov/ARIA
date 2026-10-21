# BUG-2026-10-21-early-fault-dropped: битый файл «играет» в тишине вечно

## Problem

- Actual: движок ставит Faulted-событие при неудачном открытии в очередь рендер-потока и сразу возвращает handle. Рендер-поток может отправить событие раньше, чем контроллер получит `CompleteOpen`. Тогда контроллер не знает handle и выбрасывает событие, а дек остаётся `Playing` без звука, без пометки Faulted и без EndAction.
- Expected: Faulted доходит до дека независимо от порядка: трек помечается, транспорт останавливается.
- Reproduce: флак `AriaAudioEngineTests.BrokenTrack_StopsWithoutAdvance` под нагрузкой; детерминированно `EarlyFaultTests`.

## Root Cause Analysis

- `OnStreamEvent` молча игнорировал события для handle, который ещё не привязан и не retired.
- Risk level: High: оператор видит «играет», а в зале тишина.

## Fix

- Пока открытие в полёте, события неизвестных handle буферизуются (`_earlyEvents`). `CompleteOpen` после `FinishOpen` проигрывает раннее событие своего handle и чистит буфер; устаревшие открытия удаляют свои записи.

## Acceptance Criteria

- [x] `EarlyFaultTests.FaultEmittedBeforeOpenCompletes_StillFaultsTheDeck`
