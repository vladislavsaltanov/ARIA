# 1.1 Проект вместо Плейлиста — план фазами

Решения: полный rename идентификаторов; remote-ключи старые как алиасы; папка `.aria/` в этой же эпопее.

## Карта rename (идентификаторы)

Playlist → Project, PlaylistId → ProjectId, PlaylistEntry → ProjectEntry,
PlaylistOverrides → ProjectOverrides, CreatePlaylist → CreateProject,
RenamePlaylist → RenameProject, DeletePlaylist → DeleteProject,
SetActivePlaylist → SetActiveProject, ImportPlaylist → ImportProject,
ImportPlaylistEntry → ImportProjectEntry, NormalizePlaylist → NormalizeProject,
PlaylistFormat → ProjectFormat, PlaylistVm → ProjectVm, PlaylistImportReport → ProjectImportReport.
НЕ переименовывать: EntryId, entry-команды (RemoveEntry/MoveEntry/AddEntry/SetEntryOverrides —
EntryId общий с очередью), FormatId `aria-playlist` v1, remote-ключи, SQLite-схема, UI-строки (свои фазы).

## Фазы (каждая — зелёные сборки, свой коммит)

A. Механический rename C#-идентификаторов + app.js по карте выше, поведение 1:1.
   verify: `dotnet build` 0 warnings + все 5 тестовых сборок изолированно.
B. UI-строки: «Плейлист» → «Проект» (axaml, тултипы, статусы, диалоги), имена «Новый плейлист N».
   verify: App.Tests + headless.
C. Remote: новые ключи create_project/... + старые алиасами; app.js на новые; index.html/css по месту.
   verify: Remote.Tests + ручной пульт.
D. Persistence: C#-имена + миграция SQLite-схемы; SnapshotStore DTO.
   verify: Persistence.Tests.
E. Формат v2 `aria-project` + авто-миграция v1 без потерь + привязка сценариев (scripts[] проекта, мульти).
   verify: Core.Tests + App.Tests (импорт/экспорт round-trip).
F. Папка `.aria/` (project.json + scripts/*.json) + экспорт оба варианта (ссылки + ZIP) + создание дропом папки.
   verify: App.Tests + ручной сценарий ТЗ 3.3.
   DONE 2026-09-17 (ветка feat/project-phase-f-folder): Save/Open .aria, ZIP туда-обратно,
   дроп открывает существующий и активирует созданный. Ручной пульт/сценарий — за пользователем.

## Правила фаз

- Один логический батч = один коммит; фаза зелёная целиком или не коммитим.
- Никаких комментариев; поведение меняется только в своей фазе (A — ноль поведения).
- Непроверяемое здесь (Windows-рантайм, win DLL) — в матрицу резолюций.
