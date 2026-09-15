# Story e01s03: Remote settings для global EndAction

**type:** feat
**risk:** P1
**context:** domain

**Context**: Кодек `set_default_end_action` готов,
десктоп меняет глобал из настроек, но пульт его
не видит: `ShowState` не несёт DefaultEndAction,
в веб-клиенте пункта нет. Стори доводит значение
до пульта в обе стороны.

## Zoom-out

- **Purpose**: глобал EndAction — часть Show,
  пульт читает и меняет его как десктоп.
- **Callers**: `ShowController.EmitShow`,
  `RemoteHost` (show-кадр), `app.js` (рендер).
- **Contracts**: `ShowState` — positional record;
  новая позиция чинит 3 конструкции; `OnSet`
  обязан `EmitShow`, иначе пульт не узнает.

## Requirements

#### ADDED: Пульт показывает и меняет global EndAction

**Before:** `ShowState` без DefaultEndAction;
`OnSetDefaultEndAction` молчит в шину; в `app.js`
пункта нет.
**After:** `ShowState.DefaultEndAction` едет в show-
кадре; смена шлёт `set_default_end_action` с пульта;
селект отражает текущее значение и живёт рестарт
(персист уже есть на десктопе).

## Steps

1. `ShowState.DefaultEndAction` + `EmitShow` в
   `OnSetDefaultEndAction`, Core-тест дельты
   → verify: `dotnet test tests/ARIA.Core.Tests
   --filter DefaultEndAction`
2. Селект в пульте (index.html + app.js): показ
   текущего, отправка смены
   → verify: `dotnet build`
3. RemoteHost-тест: show-кадр несёт defaultEndAction;
   кодек round-trip уже покрыт
   → verify: `dotnet test tests/ARIA.Remote.Tests`
4. Полный прогон
   → verify: `dotnet test`

## Verification Script

1. Открой пульт, найди селект действия.
2. Значение совпадает с десктопом.
3. Смени с пульта → десктоп-комбобокс обновился.
4. Смени с десктопа → пульт обновился без reload.

## Out of scope

- Per-entry override с пульта (уже есть).
- MIDI/MSC (следующий срез).

## Risks

- Позиционный record: пропущенная конструкция
  ломает билд — ловит шаг 1.
- JS без юнит-тестов: ручная проверка по скрипту.

## Acceptance

- [x] Значение в обе стороны, без reload.
- [x] `dotnet test` зелен, 0 warnings.
