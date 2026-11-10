# BUG-2026-11-10T104500: импорт трека на холодную не появляется в проекте (гонка с Control-потоком)

## Problem

Actual: первый импорт файла после старта иногда рапортует «импортировано: 1, пропущено: 0, ошибок: 0», но трек не появляется в проекте. Повторный импорт того же файла показывает «пропущено: 1» — и тогда трек появляется. То же сбоит на других треках.

Expected: после успешного импорта трек сразу появляется в выбранном проекте; повторный импорт не требуется.

How to reproduce: пустой/холодный запуск, выбрать проект, импортировать один файл (drag&drop или кнопка). Воспроизводится не всегда — гонка.

Security impact: NONE. Путь импорта не читает ничего вне выбранных файлов, новой поверхности нет.

## Root Cause Analysis

Путь: `ProjectsViewModel.ImportIntoAsync` → `AppHost.ImportTracksAsync` → `_library.Upsert(current, projects)` → `SyncShowState(current)` → `Submit(new MergeTracks(...))` → возврат в VM → VM читает `_trackSource()` (`host.Tracks` → `ShowController.Tracks`) и ищет треки по путям → `Submit(new AddEntry(...))`.

`CommandBus` работает в `BusMode.Pumped`: `Submit` только кладёт конверт в канал и возвращается; `MergeTracks` применяется фоновым pump-потоком. `ImportTracksAsync` не ждёт этого и возвращается сразу. VM читает `ShowController.Tracks` до применения `MergeTracks` → совпадений по пути нет → `AddEntry` не отправляется → трек не в проекте, хотя `ImportReport.Added == 1`.

Второй импорт: трек уже в библиотеке (`Upsert` завершился) и в контроллере (`MergeTracks` к тому моменту применён), поэтому `ImportTracksAsync` рапортует `Skipped = 1`, а VM находит трек в `_trackSource()` и добавляет его в проект. Это точно соответствует симптому «повторный импорт — появляется / пропущено».

Точка отказа — отсутствие синхронизации Shell-операции с Control-потоком. Контроллер остаётся единственным источником истины, поэтому фикс — дождаться применения команд перед возвратом, а не читать обходной источник.

Risk level: Medium. Гонка времени, окно небольшое, поэтому воспроизводится не всегда.

## TDD Fix Plan

1. **RED**: `AppHost_ImportTracksAsync_WaitsForControlFlowMerge` — заблокировать pump через `((CommandBus)host.Bus).Post(...)`, запустить `ImportTracksAsync`; пока pump заблокирован, задача НЕ должна завершаться; после разблокировки — `report.Added == 1` и трек есть в `host.Tracks` сразу (без поллинга).
   **GREEN**: после `SyncShowState(current)` добавить FIFO-barrier через `CommandBus.Post` (TCS с `TaskCreationOptions.RunContinuationsAsynchronously`) и `await` его перед возвратом. Канал FIFO с одним читателем гарантирует, что barrier выполнится после `MergeTracks`/`MarkMissing`.
   **verify**: `dotnet test tests/ARIA.App.Tests --filter AppHost_ImportTracksAsync_WaitsForControlFlowMerge`

2. **REFACTOR**: не нужен. При необходимости вынести barrier в приватный метод `AwaitBusDrainedAsync`.

## Acceptance Criteria

- [ ] После `await ImportTracksAsync` трек гарантированно доступен в `host.Tracks` (без поллинга)
- [ ] `ProjectsViewModel.ImportAudioFilesAsync` с `trackSource: () => host.Tracks` добавляет entry без повторного импорта
- [ ] Существующие тесты AppHost/VM зелёные
- [ ] 0 предупреждений

## Resolution

<!-- filled in by validate-fix -->
