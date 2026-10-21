# BUG-2026-10-21-shutdown-deadlock: выход из приложения ждёт 3 с и не закрывает движок

## Problem

- Actual: `App.ShutdownRequested` блокировал UI-поток вызовом `host.DisposeAsync().AsTask().Wait(3s)`. Продолжения `await` внутри `AppHost.DisposeAsync` (`remote.DisposeAsync`, `Task.Run(engine.Dispose)`) захватывали UI-контекст и ждали заблокированный UI-поток. Итог: каждый выход стоил 3-секундный таймаут, а аудио-устройства, SQLite и лог не закрывались штатно. Автосейв успевал только потому, что `FlushNow` вызывался до первого `await`. Тот же дедлок вешал `tools/Snapshots`.
- Expected: dispose не зависит от контекста вызывающего потока.

## Fix

- `AppHost.DisposeAsync`, `RemoteHost.DisposeAsyncCore`: `ConfigureAwait(false)`.
- `App.ShutdownRequested`: `Task.Run(() => host.DisposeAsync().AsTask()).Wait(3s)`.

## Acceptance Criteria

- [x] `ShutdownDeadlockTests.Dispose_BlockedOnUiLikeContext_CompletesInsteadOfDeadlocking`
