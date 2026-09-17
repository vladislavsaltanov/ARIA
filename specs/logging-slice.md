# Срез логирования (feat/app-log)

Цель: диагностика падений + аудит действий. Один JSON-lines файл, свой мини-логгер, ноль пакетов.

## Формат

`DataDirectory/logs/aria.log`, append, одна строка = один объект:
`{"ts":"...Z","level":"info|warn|error|debug","msg":"...","data":{...}}`.
Ротация: 5 МБ → `aria.log.1` (один бэкап). Уровень из `ARIA_LOG_LEVEL` (debug включает debug, иначе info+).
Логгер никогда не бросает; с аудио-callback не вызывается (там ноль локов — инвариант).

## API (ARIA.Core, `Aria.Core.Runtime`)

- `LogLevel { Debug, Info, Warn, Error }`
- `IAppLog { void Write(LogLevel, string message, IReadOnlyDictionary<string,string>? data); }` + расширения `Info/Warn/Error/Debug`
- `FileAppLog : IAppLog, IDisposable` — lock, fsync нет, flush на Dispose; `NullAppLog` для тестов/выключенного состояния
- Запрет секретов: пароли/токены/credentials не пишутся никогда (только факты: `remote.auth_failed`, без значений)

## Точки врезки

1. `Program.Main`: `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` → error fatal (логгер создаётся до старта Avalonia).
2. `AppHost.StartAsync/DisposeAsync`: info `host.started` (version, data dir — без секретов) / `host.stopped`.
3. `AppHost.Submit`: info `command` (`{type, client}`), кроме `TickShowClock`.
4. `AppHost` подписка на Bus: warn `command.rejected` (`{type?, reason}` из `Rejected`).
5. `ImportTracksAsync` / `RelinkTrackAsync`: warn `import.failed` / `relink.failed` (`{path}`), успех импорта — info суммарно (`{added, skipped, failed}`).
6. Remote: info `remote.started` (`{endpoint}`), warn `remote.port_fallback`.
7. Output: warn `output.device_fault` (текст, что уже показывается в UI).

## Не в срезе

Метрики/‘I/O длительности, просмотр лога из UI/пульта, отправка логов наружу, уровни на команду.

## Verify

`dotnet build` 0 warn/0 err; `dotnet test tests/ARIA.Core.Tests tests/ARIA.App.Tests` зелёные; руками: запуск, действие, краш-тест через `--selftest`-подобный прогон, `tail logs/aria.log`.
