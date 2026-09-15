# Story e01s01: Per-entry EndAction UI в плейлисте

**type:** feat
**risk:** P1
**context:** domain

**Context**: Резолюция EndAction готова
(`PlaybackSettings.Resolve`: override > track > global),
команда `SetEntryOverrides` проведена через шину,
контроллер и персист покрыты тестами. Нет только UI:
оператор не может задать действие конца на вхождение.
Стори добавляет минимальный редактор в плейлист.

## Zoom-out

- **Purpose**: `PlaylistOverrides` на `PlaylistEntry`,
  null = наследовать.
- **Callers**: `ShowController.OnSetEntryOverrides`
  (replace-целиком), `PlaybackSettings.Resolve`,
  `Mappers` round-trip. Прод-юзер команды сегодня: ноль
  (кодек парсит, UI нет).
- **Contracts**: порядок резолва неизменен; неизвестный
  entry reject; запись целиком, не патч.

## Requirements

#### MODIFIED: Действие конца задаётся на вхождение из UI

**Before:** EndAction вхождения меняется только кодом/
тестами через `SetEntryOverrides`; в UI плейлиста
редактора нет, действует глобал/трек-дефолт.
**After:** Контекст-меню строки плейлиста содержит
пункт "Действие в конце" с вариантами Наследовать/
Пауза/Стоп/Повтор/Далее; выбор шлёт `SetEntryOverrides`
через `ICommandBus`, строка показывает итог.

## Steps

1. VM-метод `SetEntryEndAction` с merge-семантикой
   (копия текущих Overrides + новый EndAction;
   Наследовать = null EndAction, не null Overrides)
   → verify: `dotnet test tests/ARIA.App.Tests
   --filter SetEntryEndAction`
2. Пункт меню "Действие в конце" в `PlaylistCenter`,
   checked-вариант = эффективный EndAction строки
   → verify: `dotnet build`
3. Headless-тест: выбор пункта меню шлёт команду
   в шину, `EntryVm` обновляется после `Rebuild`
   → verify: `dotnet test tests/ARIA.App.Tests
   --filter EntryEndActionMenu`
4. Рестарт-пруф: значение живёт reload, сброс
   возвращает глобал (покрыто LibraryStoreTests:84 +
   SetEntryEndAction_Null)
   → verify: `dotnet test`

## Verification Script

1. Старт, открой плейлист с треками.
2. Правый клик по строке → "Действие в конце".
3. Выбери Пауза → галка на Паузе.
4. Рестарт → галка на месте.
5. Выбери Наследовать → галка ушла, действует глобал.

## Out of scope

- Редактор прочих overrides (gain, cue, цвет).
- Remote UI глобала (это e01s03).
- MIDI/MSC (не в итерации).

## Risks

- Replace-целиком: VM обязана merge'ить, иначе сотрёт
  соседние overrides. Ловит шаг 1.
- Headless-флак старта (известный, AGENTS.md):
  при конфликте перезапуск.

## Acceptance

- [x] Выбор из UI переживает рестарт.
- [x] Наследовать возвращает глобал.
- [x] `dotnet build` 0 ошибок, suite зелен.
- [x] Без комментариев, 0 warnings.
