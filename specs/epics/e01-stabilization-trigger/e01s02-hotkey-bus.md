# Story e01s02: Hotkey через ICommandBus

**type:** feat
**risk:** P1
**context:** domain

**Context**: Жесты в строки действий покрыты
(`HotkeyService`), но связка действие→команда живёт
в приватном `App.DispatchHotkey` и дёргает методы
`TransportViewModel` напрямую. Стори выносит чистый
маппер действие→`Command` и шлёт плеерные хоткеи
в шину. Поведение 1:1, `toggle-script` остаётся UI.

## Zoom-out

- **Purpose**: `ICommandBus` — единственный шов
  управления; VM-команды уже шлют туда же.
- **Callers**: `App.Compose` (диспетч), будущие
  MIDI/MSC-источники — тот же маппер.
- **Contracts**: маппинг 1:1 без смены поведения;
  `CanExecute` у VM всегда true, прямой submit
  эквивалентен.

## Requirements

#### MODIFIED: Плеерные хоткеи идут в шину, не в VM

**Before:** `DispatchHotkey` вызывает
`PlayCommand/PauseCommand/...` транспорта; маппинг
непокрыт, переиспользовать не из чего.
**After:** `HotkeyCommands.ToCommand` маппит действие
в `Command` (null = UI-local); App шлёт итог
в `host.Bus`; маппинг и эффект покрыты тестами.

## Steps

1. `HotkeyCommands.ToCommand` + theory-тесты маппинга
   → verify: `dotnet test tests/ARIA.App.Tests
   --filter HotkeyCommands`
2. Интеграция: мапленная команда через Inline-шину
   меняет состояние (panic/lock/reset-clock)
   → verify: `dotnet test tests/ARIA.App.Tests
   --filter HotkeyCommands`
3. `App.DispatchHotkey` шлёт в `host.Bus`,
   toggle-script остаётся окном
   → verify: `dotnet build`
4. Полный прогон
   → verify: `dotnet test`

## Verification Script

1. Старт, Space = play, Esc = pause.
2. Ctrl+N = next, Ctrl+Shift+P = panic.
3. Ctrl+T = шторка сценария (UI, без шины).
4. Ctrl+L = замок туда-обратно.

## Out of scope

- Путь Space в `MainWindow` (уже VM→шина, эквивалент).
- MIDI/MSC (следующий срез).
- Редактор хоткеев (существует).

## Risks

- P1, не P0: поведение 1:1, маппер чистый,
  критический panic-путь уже покрыт ядром.
- Двойной Play при повторном Space: как было
  (VM-команда без гарда).

## Acceptance

- [x] Все 9 действий маппятся как раньше.
- [x] `toggle-script` шину не трогает.
- [x] `dotnet test` зелен, 0 warnings.
