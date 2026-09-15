# Story e01s06: Настройки — боковая навигация и секции

**type:** refactor
**risk:** P1
**context:** ui

**Context**: `SettingsDialog.axaml` — 200 строк, 6 секций
в одном файле; добавление раздела правит монолит.
Стори выносит секции в `Views/SettingsSections/`
и ставит боковую навигацию с реестром. VM shared —
поведение 1:1, тесты целы.

## Zoom-out

- **Purpose**: диалог — шелл (навигация + выбор),
  секции — контент.
- **Callers**: `MainWindow` (фабрика диалога),
  `SettingsDialogHeadlessTests`.
- **Contracts**: те же биндинги, та же VM;
  Esc-закрытие и запись жестов без изменений.

## Requirements

#### ADDED: Разделы настроек с боковой навигацией

**Before:** Один скролл-монолит; новый раздел =
правка 200-строчного файла.
**After:** Слева список разделов (Пульт, Жесты,
Часы, Формат строк, Воспроизведение, Движок),
справа контент; новый раздел = UserControl + 1
строка в реестре.

## Steps

1. Секции в `SettingsSections/` (RemotePanel reuse),
   шелл с nav-списком и выбором
   → verify: `dotnet build`
2. Headless: клик по разделу меняет контент,
   Esc закрывает, запись жестов жива
   → verify: `dotnet test tests/ARIA.App.Tests
   --filter SettingsDialog`
3. Реестр секций (порядок + фабрики) + тест:
   все пункты реестра создают контролы
   → verify: `dotnet test tests/ARIA.App.Tests
   --filter SettingsSections`
4. Полный прогон
   → verify: `dotnet test`

## Verification Script

1. Открой настройки — слева 6 разделов.
2. Кликай разделы — контент меняется.
3. Жесты: запись/конфликт как раньше.
4. Esc закрывает.

## Out of scope

- Распил VM (это e01s07).
- Новые пункты настроек.
- Remote UI глобала (e01s03).

## Risks

- Контекст DataContext наследуют секции — проверить
  headless-тестом (шаг 2).
- Размер окна: контент уже скроллится, нав фиксирован.

## Acceptance

- [x] 6 разделов, порядок как был.
- [x] Биндинги и команды без изменений.
- [x] Новый раздел = файл + 1 строка.
- [x] `dotnet test` зелен, 0 warnings.
