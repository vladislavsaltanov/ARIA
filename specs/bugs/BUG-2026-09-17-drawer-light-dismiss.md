# BUG-2026-09-17-drawer-light-dismiss: клик мимо панели сценариев закрывает её

## Problem

Actual: при открытой панели сценариев любой клик вне неё (по трекам, пустому
месту) сразу закрывает панель.

Expected: клик вне панели её не закрывает. Закрытие только явное: крестик,
кнопка «Сценарий», горячая клавиша Ctrl+T.

Repro: открыть панель сценариев → кликнуть по центру → панель закрылась.

Security impact: NONE. Локальное UI-поведение.

## Root Cause Analysis

Путь: туннельный `PointerPressed` на корне окна → `OnRootPointerPressed`
закрывает drawer при любом источнике вне ScriptPanel/PaneResizer/ScenarioButton.
Light-dismiss для overlay-панели с несохранённым редактированием (CommitOpenEdit
перед закрытием) — неверный паттерн: одно мимо-клик теряет контекст работы.

Risk level: Low. Удаление одного хендлера; пути закрытия через ToggleScriptPane
не тронуты.

## TDD Fix Plan

1. **RED**: headless-тест — клик мимо открытой панели оставляет её открытой.
   **GREEN**: удалить `OnRootPointerPressed` и его регистрацию.
   **verify**: `dotnet test tests/ARIA.App.Tests`

**REFACTOR**: none.

## Acceptance Criteria

- [ ] Клик вне панели не закрывает её
- [ ] Крестик/кнопка/хоткей закрывают как раньше
- [ ] Существующие тесты зелёные

## Resolution

Fixed 2026-09-17, branch feat/project-phase-f-folder.
Two closers, not one: our OnRootPointerPressed (press) plus SplitView's own
overlay dismiss (release, TopLevel.PointerReleased subscription). Removed ours;
theirs is cancelled via PaneClosing unless the close is explicit
(_explicitPaneClose around ToggleScriptPane). Drawer closes only via close
button, Scenario button, hotkey, Esc (built-in). Lesson: headless MouseDown
alone is not a click — test does Down+Up now.
Tests: App 337 isolated green.

Follow-up (same branch): panel stayed open but outside clicks never arrived —
SplitView template shows a transparent LightDismissLayer over content in
overlay mode; it eats presses (releases still bubble, hence the earlier
confusion). A Window.Styles override lost to the theme; HideDismissLayer sets
a local IsVisible=false on open (single opener: ToggleScriptPane).
Tests: App 338 isolated green.
