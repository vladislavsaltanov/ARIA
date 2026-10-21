# BUG-2026-10-21-bus-pump-death: одно исключение в обработчике навсегда глушит управление

## Problem

- Actual: исключение в `IShowHandler.Handle`, в marshaled-работе движка (`Post`) или в любом подписчике `Subscribe` вылетает из цикла насоса `CommandBus`. Задача насоса падает, дальше все команды — включая PANIC — молча пишутся в канал, который никто не читает. Снаружи приложение выглядит живым: UI рисуется, кнопки нажимаются, звук продолжает играть без управления.
- Expected: сбой одной команды изолирован. Команда отклоняется (`Rejected` с причиной `internal-error`), сбой пишется в лог, насос продолжает работу. Подписчик, бросивший исключение, не мешает другим подписчикам и обработчику.
- Reproduce: обработчик бросает на любой команде; следующая команда не исполняется никогда.

## Root Cause Analysis

- `PumpLoopAsync` ловит только `OperationCanceledException`.
- `Publish` вызывает подписчиков синхронно внутри `Handle`; исключение UI-подписчика рвёт мутацию состояния посередине и уходит в насос.
- Risk level: Critical: «шоу не должно остановиться» нарушается молча.

Security impact: LOW: удалённый пульт мог бы уронить управление командой, которая провоцирует исключение в обработчике.

## Fix

- `Dispatch`/`Run` обёрнуты в изоляцию: лог `bus.handler_failed` / `bus.work_failed`, `Rejected(internal-error)` для команд.
- `Publish` изолирует каждого подписчика: лог `bus.subscriber_failed`.
- `CommandBus` принимает `IAppLog`; `AppHost` пробрасывает текущий лог.

## Acceptance Criteria

- [x] Бросающий обработчик не останавливает насос (`BusResilienceTests.PumpedMode_HandlerThrows_KeepsProcessingLaterCommands`)
- [x] Бросающая работа `Post` не останавливает насос (`PumpedMode_PostedWorkThrows_KeepsProcessingLaterCommands`)
- [x] Бросающий подписчик не лишает событий остальных (`ThrowingSubscriber_DoesNotStarveOtherSubscribersOrHandler`)
- [x] Existing tests still pass
