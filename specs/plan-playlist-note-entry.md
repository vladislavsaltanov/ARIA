# План: NoteEntry в плейлисте

> Исполняется последовательно, TDD там, где есть поведение. Спека —
> `specs/playlist-note-entry.md`.

**Goal:** текстовая строка (заметка) в порядке плейлиста, без Track, без
воспроизведения, с инлайн-правкой и раскрытием.

**Architecture:** union `ProjectEntry(Id, Content)`; курсор плейбэка
пропускает заметки; снапшот/JSON/SQLite/remote-wire несут `kind`.

**Verify (каждый таск):** указанная команда зелёная и `dotnet build`
без предупреждений.

---

### T1. Модель: union без изменения поведения

**Файлы:** `src/ARIA.Core/Model/Playlist.cs`,
`src/ARIA.Core/Model/PlaybackSettings.cs`,
`src/ARIA.Core/Runtime/ShowController.cs`,
`src/ARIA.Core/Model/PlaylistFormat.cs`,
`src/ARIA.Persistence/Mappers.cs`,
`src/ARIA.App/ViewModels/PlaylistsViewModel.cs`,
`src/ARIA.Remote/RemoteClient/app.js`, все тесты с `new ProjectEntry(`.

**Сигнатуры:**
```csharp
public sealed record ProjectEntry(EntryId Id, EntryContent Content);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TrackContent), "track")]
[JsonDerivedType(typeof(NoteContent), "note")]
public abstract record EntryContent;

public sealed record TrackContent(TrackId TrackId, ProjectOverrides? Overrides = null) : EntryContent;
public sealed record NoteContent(string Text) : EntryContent;

public static PlaybackSettings Resolve(TrackContent content, Track track, EndAction defaultEndAction = EndAction.Advance);
```

Механика: `entry.TrackId` → `((TrackContent)entry.Content).TrackId` в
местах, где трек уже гарантирован; `entry.Overrides` → тот же cast;
`entry with { Overrides = x }` → `entry with { Content = content with { Overrides = x } }`.
В `app.js`: `entryTrack(entry)`/`entryOverrides(entry)` вместо
`entry.trackId`/`entry.overrides` (чтение `content.kind === "track"`).

- [ ] Тесты падают на новой сигнатуре → правим конструкторы, поведение не меняем.
- [ ] `dotnet build` → 0 warnings.
- [ ] `dotnet test` → всё зелёное (рефакторинг без изменения поведения).

### T2. Core: команды заметки, пропуск в плейбэке, guard'ы

**Файлы:** `src/ARIA.Core/Model/Playlist.cs` (фабрика),
`src/ARIA.Core/Commands/Commands.cs`,
`src/ARIA.Core/Runtime/ShowController.cs`,
`tests/ARIA.Core.Tests/NoteEntryTests.cs` (новый),
`tests/ARIA.Core.Tests/PlaylistEditTests.cs`.

**Сигнатуры:**
```csharp
public sealed record AddNoteEntry(ProjectId Project, string Text, int? Index = null) : Command;
public sealed record SetNoteText(EntryId Entry, string Text) : Command;

public sealed record NoteContent(string Text)
{
    public const int MaxLength = 2000;
    public static bool TryCreate(string? raw, out NoteContent? note);
}
```
`TryCreate`: trim, пустое/`>MaxLength` → false.

Поведение:
- `NextTrackIndex(Project, int from) -> int` (-1 если треков дальше нет);
  им пользуются `StartFromOrder`, `HasNext`, `PeekNext`.
- `StartProjectEntry` кастует `TrackContent` (вызывается только с индексом трека).
- `JumpTo`, `EnqueueEntry`, `SetEntryOverrides`, `SetEntryAudio`,
  `SetEntryMarkers`, `SetEntryMarkers`, `StartPreviewTrack` на заметке →
  `Reject("note-entry")`.
- `RemoveEntry`, `MoveEntry`, undo (`SetNoteText` пишет старый текст в undo),
  `ValidateShow` (заметка без трека валидна), `BuildDigest` (заметки не в digest).
- `AddNoteEntry`: bounds как у `AddEntry`, пустая/длинная строка → `bad-text`.

- [ ] Тесты: заметка пропускается `Next`/auto-advance; `HasNext` false,
  если остались только заметки; `PeekNext` смотрит за заметку; replay не
  задевает заметки; команды на заметке реджектятся; add/set/undo/move/remove.
- [ ] `dotnet test tests/ARIA.Core.Tests` → зелёно.

### T3. Persistence: снапшот + SQLite + миграция

**Файлы:** `src/ARIA.Persistence/Mappers.cs`,
`src/ARIA.Persistence/LibraryStore.cs`,
`tests/ARIA.Persistence.Tests/NoteEntryPersistenceTests.cs` (новый),
`tests/ARIA.Persistence.Tests/ProjectSchemaMigrationTests.cs`.

DTO: `EntryDto { Guid Id; Guid? TrackId; OverridesDto? Overrides; string? Text; }`;
пустой `Text` у заметки → запись отбрасывается при чтении.

SQLite: `project_entries.track_id` становится nullable, добавляется
`text TEXT`. Миграция при открытии: если колонки `text` нет — `ALTER TABLE
ADD COLUMN text TEXT`; если `track_id` NOT NULL — пересборка таблицы
(CREATE/INSERT SELECT/DROP/RENAME), идемпотентно.

- [ ] Тесты: round-trip заметки через `SqliteLibraryStore`; чтение старой
  БД (track_id NOT NULL) → миграция сохраняет треки и открывает заметки;
  снапшот-json с заметкой и без поля `text`.
- [ ] `dotnet test tests/ARIA.Persistence.Tests` → зелёно.

### T4. Формат `.aria-project.json` v3

**Файлы:** `src/ARIA.Core/Model/PlaylistFormat.cs`,
`tests/ARIA.Core.Tests/PlaylistFormatTests.cs`.

`ProjectFileEntry`: `File` nullable, `Kind` (`track` по умолчанию, `note`),
`Text`. `CurrentVersion = 3`, импорт принимает 1/2/3; для `kind=note`
`file` не требуется, `text` обязателен и валиден; экспорт пишет v3.

- [ ] Тесты: v3 round-trip с заметкой; импорт v2 без `kind`; note без
  текста/с пустым текстом → `ProjectFormatException`; экспорт содержит
  `kind` и `text`.
- [ ] `dotnet test tests/ARIA.Core.Tests` → зелёно.

### T5. App VM: строки, правка, счётчик, поиск

**Файлы:** `src/ARIA.App/ViewModels/PlaylistsViewModel.cs`,
`tests/ARIA.App.Tests/NoteEntryViewModelTests.cs` (новый).

`EntryVm` получает `bool IsNote`, `string? NoteText`, `bool IsExpanded`;
для заметки: `Duration = 0`, `Waveform = null`, `IsFaulted = false`,
`RowText` = текст, `Position` = сквозной номер.
`AddNoteCommand` (после выбранной строки или в конец), `EditNote(entry, text)`,
`ToggleNote(entry)`; счётчик `N треков · M заметок · время` (время без
заметок); поиск по `NoteText`; `EnqueueEntry`/`SetEndAction`/`SetEntryBacking`/
`CreateEntryAudioEditor` на заметке — no-op.

- [ ] Тесты: построение заметки, счётчик, поиск, add/edit через фейк-бус.
- [ ] `dotnet test tests/ARIA.App.Tests` (headless; при флейке — перезапуск).

### T6. XAML: чип, меню, раскрытие, инлайн-редактор

**Файлы:** `src/ARIA.App/Views/PlaylistCenter.axaml`,
`src/ARIA.App/Views/PlaylistCenter.axaml.cs`,
`src/ARIA.App/AppHost.cs` (проброс, если нужен).

- Чип «Заметка» (`icon_script`) рядом со «Сценарий»; пункт «Добавить
  заметку» в контекстном меню области (пустое место и строки).
- Шаблон строки: для заметки — номер, текст с обрезкой, «—», глиф
  `icon_script` 14 `BrushFaint`; бейдж/fault/волна скрыты.
- Клик — toggle раскрытия (под строкой Border с wrap-текстом);
  двойной клик/F2 — TextBox в строке; Enter — commit, Shift+Enter — перенос,
  Esc — отмена; пустой текст не сохраняется.
- Контекстное меню заметки: «Править», «Убрать из проекта».

- [ ] Headless-тест: заметка рисуется, клик раскрывает, F2/двойной клик
  открывает редактор, Enter сохраняет через бус.
- [ ] `dotnet test tests/ARIA.App.Tests` → зелёно.

### T7. Remote: рендер заметки

**Файлы:** `src/ARIA.Remote/RemoteClient/app.js`,
`src/ARIA.Remote/RemoteClient/app.css`,
`tests/ARIA.Remote.Tests/ShowNoteWireTests.cs` (новый).

- `entryRow`: `content.kind === "note"` → строка текста, без ▶/⚙/оверрайдов,
  ↑↓/✕ остаются; заметка не участвует в `digestName`.
- Wire-тест: `ShowState` с заметкой сериализуется как
  `"content":{"kind":"note","text":"…"}` с теми же `JsonSerializerOptions`.

- [ ] `dotnet test tests/ARIA.Remote.Tests` → зелёно; `node --check` на app.js.

### T8. Docs, полный прогон, коммит

**Файлы:** `CONTEXT.md` (термин `NoteEntry`), `DESIGN.md` (строка/раскрытие),
`CHANGELOG.md`, `specs/playlist-note-entry.md` (status → implemented).

- [ ] `dotnet build` → 0 warnings; `dotnet test` → всё зелёное (флаки —
  перезапуск по AGENTS.md).
- [ ] `dotnet run --project tools/Snapshots -- /tmp/aria-note-snap` —
  рендер состояния с заметкой (если инструмент поддерживает сценарий).
- [ ] Коммит + push; отчёт: что руками, что не проверено.
