# BUG-2026-09-17-selection-activates: выбор проекта не активирует его

## Problem

Actual: клик по проекту в боковом списке меняет только подсветку (SelectedProject).
ActiveId остаётся прежним, поэтому панель сценариев показывает чужие сценарии,
а клик по упоминанию играет трек «не из открытого проекта». Кнопка активации
(ActivateProjectCommand) нигде не забиндена — активировать проект из UI вообще
невозможно, кроме дропа папки.

Expected: выбранный проект становится активным; панель сценариев показывает
его сценарии (у нового — пусто); упоминания играют только треки активного проекта.

Repro: два проекта A (активен, со сценарием) и B. Кликнуть B в списке.
Панель сценариев по-прежнему показывает сценарий A; его упоминания играются.

Security impact: NONE. Локальный UI-флоу, trust boundary не пересекается.

## Root Cause Analysis

Путь: ListBox.SelectedItem → SelectedProject → OnSelectedProjectChanged только
перерисовывает центр. SetActiveProject не сабмитится ниоткуда в UI (биндинга
ActivateProjectCommand не было никогда — проверено git log -S по Views/).
Все нижележащие механизмы уже корректны и завязаны на ActiveId: фильтр панели
(SyncScripts), IsKnownTrack, гард PlayTrack (track-not-in-playlist). Реализация
привязки не потеряна — она недостижима: ActiveId застревает.

Очередь (EnqueueTrack) и превью (StartPreviewTrack) осознанно глобальные:
тесты TrackDigest_CoversProjectQueueDeckAndMentions,
EnqueueLibraryOnlyTrack_EmitsShowDelta и StartPreviewTrack_RoutesAudioToEngine
фиксируют это поведение. Не трогать.

Risk level: Low. Одна точка входа (хендлер выбора), гард по ActiveId исключает
шторм команд при Rebuild (новые инстансы ProjectVm каждый раз).

## TDD Fix Plan

1. **RED**: тест VM — выбор другого проекта меняет ActiveId, панель показывает
   его (пусто для нового).
   **GREEN**: OnSelectedProjectChanged сабмитит SetActiveProject при расхождении.
   **verify**: `dotnet test tests/ARIA.App.Tests`

**REFACTOR**: none.

## Acceptance Criteria

- [ ] Клик по проекту активирует его, панель сценариев следует
- [ ] Упоминания чужого проекта не видны и не играются после переключения
- [ ] Повторные Rebuild без смены выбора команд не шлют
- [ ] Существующие тесты зелёные

## Resolution

<!-- filled in by validate-fix -->
