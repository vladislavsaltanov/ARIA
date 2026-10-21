# BUG-2026-10-21-ui-fault-crash: исключение в UI-обработчике роняет приложение посреди шоу

## Problem

- Actual:
  - Не подписан `Dispatcher.UIThread.UnhandledException`. Любое необработанное исключение в обработчике кнопки, биндинге или `Post` на UI-поток завершает процесс; заодно останавливается аудио-движок, который сам по себе исправен.
  - Fatal-хендлеры (`AppDomain.UnhandledException`) ставились только при включённом логе и ничего не сохраняли.
  - Падение `AppHost.StartAsync` (нет прав на папку данных, битая БД) терялось в `_ = ComposeAsync(...)`: окно не появлялось, процесс висел без UI.
  - `FileAppLog.CloseWriter` в catch мог бросить второй раз (диск полон).
- Expected: UI-сбой логируется, шоу продолжается. Перед неизбежным падением процесса шоу сохраняется. Сбой старта показывает понятное окно.

## Fix

- `App`: `Dispatcher.UIThread.UnhandledException` → `AppHost.ReportUiFault` (лог `ui.unhandled`), `Handled = true`. Решение осознанное: для плеера шоу продолжить работу лучше, чем упасть. Аудио и Control-поток от UI не зависят.
- `AppHost`: fatal-хендлеры ставятся всегда; `OnFatal` пишет `fatal.unhandled` и делает аварийный `FlushNow` автосейва.
- `App.ComposeAsync`: исключение старта → лог + окно «ARIA не запустилась» (шоу на диске не тронуто), закрытие → `Shutdown(1)`.
- `FileAppLog.CloseWriter`: dispose обёрнут.

## Acceptance Criteria

- [x] `AppHostFatalTests.Fatal_FlushesShowImmediately_EvenWithLoggingDisabled`
- [x] `AppHostFatalTests.ReportUiFault_WithLoggingDisabled_DoesNotThrow`
- [ ] Ручная проверка окна сбоя старта (не проверено в песочнице: GUI)
