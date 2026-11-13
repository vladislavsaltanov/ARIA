# Заметка в плейлисте (NoteEntry)

## Status

Реализован (2026-11-13). Дизайн ниже — что и как сделано.

Отличие от первой редакции: новая заметка создаётся с текстом
«Заметка» и сразу становится выбранной строкой; правка — F2 или
двойной клик (автооткрытие редактора после создания не делалось:
контейнер строки появляется только после перестройки списка).

## Goal

Дать оператору класть в порядок плейлиста свободный текст (заметка,
кусок сценария, ремарка между треками) как отдельную строку — без
аудиофайла, без Track, без влияния на воспроизведение.

## Non-goals

- Заметка не играет, не идёт в Queue, не показывается в шапке/табло.
- Нет времени, меток, overrides, звука, длительности, цвета.
- Создание/правка заметок с браузерного пульта — не в этой итерации
  (remote только рисует их строкой).

## Decisions (утверждены пользователем)

1. Строка 44 px с обрезкой; полный текст раскрывается по клику под строкой.
2. При воспроизведении заметка молча пропускается.
3. Правка — инлайн в строке (двойной клик/F2), Enter — сохранить,
   Shift+Enter — перенос, Esc — отмена.
4. Модель — union, не «плоская запись с nullable-полями».

## Domain

`ProjectEntry` становится `Id + Content`; содержимое — закрытый union:

```csharp
public sealed record ProjectEntry(EntryId Id, EntryContent Content);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TrackContent), "track")]
[JsonDerivedType(typeof(NoteContent), "note")]
public abstract record EntryContent;

public sealed record TrackContent(TrackId TrackId, ProjectOverrides? Overrides = null) : EntryContent;

public sealed record NoteContent(string Text) : EntryContent;
```

`Content`-обёртка вместо прямого `TrackEntry | NoteEntry` — потому что
STJ-полиморфизм требует базу без позиционного `Id`, а повторное
объявление `Id` в наследниках даёт hiding-предупреждение при
`TreatWarningsAsErrors`. Union остаётся тем же: две формы, невалидных
состояний нет.

Инварианты:
- `NoteContent.Text` после trim непустой, ≤ 2000 символов (граница
  парса, проверяется в командах и формате).
- Заметка не имеет ни Track, ни Overrides, ни Duration.

## Playback

`ShowController` читает `Content` вместо `TrackId`:

- `StartProjectEntry`, `PeekNext`, `HasNext`, `StartFromOrder` — курсор
  пропускает `NoteContent` вперёд до первого трека; если треков дальше
  нет — «следующего» нет.
- `JumpTo`, `EnqueueEntry`, `StartPreviewTrack`, `SetEntryOverrides`,
  `SetEntryAudio`, `SetEntryMarkers` на заметке → `Reject("note-entry")`.
- `RemoveEntry`, `MoveEntry`, undo, `ValidateShow` работают с заметкой
  как с обычным вхождением (без проверки Track).
- `BuildDigest`, Show clock, Queue — без изменений.

Новые команды:
- `AddNoteEntry(ProjectId Project, string Text, int? Index = null)`
- `SetNoteText(EntryId Entry, string Text)`

## Persistence

- Snapshot DTO: `EntryDto { Id, TrackId?, Overrides?, Text? }`; пустой
  `TrackId` → `NoteContent`. Старые снапшоты читаются без миграции
  (новые поля аддитивны, версии в документе нет).
- `.aria-project.json` → version 3: `kind: "track" | "note"`,
  `file` становится nullable, добавляется `text`. Импорт принимает
  v1 (legacy), v2 и v3; экспорт пишет v3.
- Remote wire — тот же union через STJ (`content.kind`); `app.js`
  рисует заметку строкой без ▶/⚙, с ↑↓/✕.

## UI (десктоп)

- Чип «Заметка» в шапке плейлиста рядом со «Сценарий» + пункт
  «Добавить заметку» в контекстном меню области.
- Строка заметки: без номера (нумерация — только по трекам), текст
  в колонке ТРЕК (обрезка), «—» в колонке ВРЕМЯ, глиф `icon_script`
  14 px `BrushFaint` в колонке ФОРМА, полупрозрачные линии сверху и
  снизу (`BrushNoteLine`). Бейдж «конец», иконка fault, волна — скрыты.
- Клик — раскрыть/свернуть блок с полным текстом (TextWrapping=Wrap,
  13 px, BrushFg, отступ 10,2,12,10).
- Двойной клик / F2 — TextBox в строке (AcceptsReturn), Enter —
  сохранить, Shift+Enter — перенос, Esc — отмена. Черновик живёт в
  `ProjectsViewModel.NoteDraft` (TwoWay), поэтому сохранение
  срабатывает и по клику в любом месте вне поля (tunnel
  PointerPressed), по потере фокуса, при переключении строки/проекта
  и при раскрытии/создании заметки.
- Контекстное меню заметки: «Править», «Убрать из проекта»; «В очередь»,
  «Действие в конце», «Звук…» скрыты.
- Нумерация сквозная только по трекам: заметка номер не занимает и
  номеров треков не сдвигает.
- Счётчик: `N треков · M заметок · время` (время — только треки).
- Поиск матчит текст заметки.

## Tests

- Core: пропуск заметок при Next/auto-advance/HasNext/PeekNext; reject
  команд на заметке; add/set text + валидация (пустой, > 2000); undo;
  формат v3 round-trip, импорт v2, валидация note-entry.
- Persistence: round-trip заметки; старый снапшот без `Text`/`TrackId`.
- App: `EntryVm` заметки (нет длительности/волны/fault), счётчик,
  поиск по тексту, создание и правка через `ICommandBus`.
- Remote: codec не меняется для чтения; сериализация `ShowState`
  с `content.kind` проверяется снимком состояния.

## Docs

CONTEXT.md — термин `NoteEntry`; DESIGN.md — строка и раскрытие.
