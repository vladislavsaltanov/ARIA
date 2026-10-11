# Метки на seek-панели — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Оператор ставит, переименовывает, двигает, удаляет и откатывает
именованные метки прямо на волне seek-панели; метка останавливает
воспроизведение в своей точке.

**Architecture:** Метка живёт на `TrackDefaults` и переопределяется на
`ProjectOverrides` вхождения. Core владеет состоянием и отменой (стек
обратных команд), движок — только детекцией пересечения: он получает
отсортированный массив меток и останавливает поток на первой пройденной.
Интерфейс рисует полосу меток над волной и переводит жесты в команды.

**Tech Stack:** .NET 10, Avalonia 12.1.2, xUnit, Avalonia.Headless, SQLite.

**Spec:** `docs/superpowers/specs/2026-10-12-markers-seek-panel-design.md`

## Global Constraints

- Комментариев в коде нет, включая XML-doc. Контракты — именами и тестами.
- File-scoped namespaces, records, иммутабельность; коллекции в домене не
  мутируются.
- 0 предупреждений (`TreatWarningsAsErrors`), полный `dotnet test` зелёный
  перед коммитом.
- PlayerState мутирует только на Control-потоке (владелец `ShowController`).
- В Render и аудио-callback: ноль аллокаций и локов — тест
  `MixerBusTests.Render_NoAllocations_InSteadyState`.
- Тесты только через интерфейсы (`ICommandBus`, `IAudioEngine`, `ILibraryStore`).
- Тексты интерфейса русские, идентификаторы кода английские.
- Терминология — `CONTEXT.md`; визуал — `DESIGN.md`.
- Имя метки ≤ 48 символов, меток на файл ≤ 64, позиция в [0, duration],
  позиции уникальны.

## Review Focus

1. Метка ровно в позиции старта потока (`CueIn` или точка автостопа):
   воспроизведение не должно вставать на ней же повторно — иначе трек
   непроходим после первого автостопа.
2. Откат назад через метку: автостоп обязан сработать снова, иначе метка
   «одноразовая».
3. Правка меток во время игры: звук не прерывается, но следующая метка
   впереди playhead'а обязана сработать.
4. Вхождение без override и вхождение с override, у которого есть другие
   поля (имя, цвет): правка меток не должна терять чужие поля, а отмена —
   оставлять пустой override-рекорд.
5. Старый show.json / ZIP / браузерный `set_entry_overrides` без меток:
   чтение не падает, а правка вхождения из браузера не стирает метки.

---

### Task 1: Модель, валидация, команды

**Files:**
- Modify: `src/ARIA.Core/Model/Playlist.cs:10` (`ProjectOverrides`)
- Modify: `src/ARIA.Core/Model/PlaybackSettings.cs` (`EffectiveSettings.Resolve`)
- Create: `src/ARIA.Core/Model/MarkerValidation.cs`
- Modify: `src/ARIA.Core/Commands/Commands.cs`
- Modify: `src/ARIA.Core/State/StateEvents.cs:24` (`DeckContent`)
- Modify: `src/ARIA.Core/Playback/EnginePort.cs` (`IAudioEngine`, `StreamEvent`)
- Test: `tests/ARIA.Core.Tests/MarkerValidationTests.cs` (new),
  `tests/ARIA.Core.Tests/EffectiveSettingsTests.cs`

**Interfaces:**
- Consumes: `Marker(string Name, TimeSpan Position, MarkerAction Action)`.
- Produces:
  - `ProjectOverrides(..., ImmutableArray<Marker>? Markers = null)`
  - `DeckContent(..., ImmutableArray<Marker> Markers = default)`
  - `StreamEvent(StreamHandle Handle, StreamEventKind Kind, StreamEndReason Reason, string? Detail = null, TimeSpan MarkerPosition = default)`
  - `static string? MarkerValidation.Validate(ImmutableArray<Marker> markers, TimeSpan duration)`
  - `SetTrackMarkers(TrackId Track, ImmutableArray<Marker>? Markers)`,
    `SetEntryMarkers(EntryId Entry, ImmutableArray<Marker>? Markers)`,
    `Undo()`
  - `void IAudioEngine.SetMarkers(StreamHandle handle, ImmutableArray<MarkerSpec> markers)`
    — default no-op в интерфейсе, чтобы существующие фейки не менялись.

- [ ] **Step 1: Failing tests**

`EffectiveSettingsTests`: вхождение с `Markers: [m]` перебивает метки Track;
`Markers: []` даёт пустой список; `Markers: null` наследует Track.
`MarkerValidationTests` (Theory): пустое имя → `marker-name-empty`; имя из 49
символов → `marker-name-too-long`; позиция `-1 тик` → `marker-position-out-of-range`;
позиция `duration + 1 тик` → `marker-position-out-of-range`; две метки в одной
позиции → `marker-position-duplicate`; 65 меток → `marker-too-many`; 64 метки в
диапазоне → `null`.

- [ ] **Step 2: Run and see them fail**

`dotnet test tests/ARIA.Core.Tests` → компиляция падает на отсутствующих
`Markers` и `MarkerValidation`.

- [ ] **Step 3: Implement**

`MarkerValidation` рядом с `AudioValidation`: один проход, проверки в порядке
из тестов, `ImmutableArray<Marker>.IsDefaultOrEmpty` → `null` (пустой список
валиден). Дедупликация по `Position` через `HashSet<long>` тиков.
Команды — три записи в `Commands.cs`. `DeckContent` — поле `Markers` в конце
списка параметров (позиционные вызовы в тестах не ломаются).

- [ ] **Step 4: Run tests**

`dotnet test tests/ARIA.Core.Tests` → PASS, `dotnet build` без предупреждений.

- [ ] **Step 5: Commit**

```bash
git add src/ARIA.Core tests/ARIA.Core.Tests
git commit -m "feat: метки вхождения, валидация и команды разметки"
```

---

### Task 2: Персистентность, ZIP, браузер

**Files:**
- Modify: `src/ARIA.Persistence/Mappers.cs:146,224,243` (`OverridesDto`)
- Modify: `src/ARIA.Core/Model/PlaylistFormat.cs` (`ProjectFileEntry`, `ToOverrides`, `ToFileEntry`, `ValidateEntry`)
- Modify: `src/ARIA.Remote/CommandCodec.cs:263` (`ParseOverrides`)
- Modify: `src/ARIA.Remote/RemoteClient/app.js:~1040` (`applyOverrides`)
- Test: `tests/ARIA.Persistence.Tests/LibraryStoreTests.cs`,
  `tests/ARIA.Core.Tests/PlaylistFormatMarkerTests.cs` (new),
  `tests/ARIA.Remote.Tests/EntryOverridesMarkersTests.cs` (new)

**Interfaces:**
- Consumes: `ProjectOverrides.Markers`, `MarkerValidation.Validate`.
- Produces: `OverridesDto.Markers` (`List<MarkerDto>?`), `ProjectFileEntry.Markers`
  (`List<ProjectFileMarker>?` с полями `name`, `position`, `action`).

- [ ] **Step 1: Failing tests**

- `LibraryStoreTests`: вхождение с метками переживает `Save`/`Load`;
  `OverridesDto` без меток читается как `null`; show.json — то же (через
  `JsonSnapshotStore`).
- `PlaylistFormatMarkerTests`: экспорт/импорт ZIP сохраняет метки вхождения;
  ZIP с пустым именем метки → `ProjectFormatException` с текстом
  `bad-marker: marker-name-empty`; ZIP со 65 метками → `bad-marker: marker-too-many`.
- `EntryOverridesMarkersTests`: JSON `set_entry_overrides` с массивом
  `markers` даёт `ProjectOverrides.Markers` с той же позицией; JSON без поля
  даёт `null` и не падает.

- [ ] **Step 2: Run and see them fail**

`dotnet test tests/ARIA.Persistence.Tests` → метки не переживают круг.

- [ ] **Step 3: Implement**

`OverridesDto.Markers` + маппинг в обе стороны в `Mappers`. `ProjectFileEntry`
получает `Markers`, `ToOverrides` включает их в условие «всё null → null» и в
конструктор, `ToFileEntry` пишет обратно, `ValidateEntry` зовёт
`MarkerValidation.Validate(markers, TimeSpan.MaxValue)` (длительность на
импорте неизвестна) и кидает `ProjectFormatException($"bad-marker: {reason}")`.
`ParseOverrides` читает необязательный массив `markers`; `app.js` в
`applyOverrides` добавляет `if (o.markers) overrides.markers = o.markers;`.

- [ ] **Step 4: Run tests**

`dotnet test tests/ARIA.Persistence.Tests tests/ARIA.Core.Tests tests/ARIA.Remote.Tests`.

- [ ] **Step 5: Commit**

```bash
git add src/ARIA.Persistence src/ARIA.Core/Model/PlaylistFormat.cs src/ARIA.Remote tests
git commit -m "feat: метки вхождения в SQLite, show.json, ZIP и кодеке пульта"
```

---

### Task 3: Движок — все метки и живая замена

**Files:**
- Modify: `src/ARIA.Audio/MixerBus.cs:196-202,453-482`
- Modify: `src/ARIA.Audio/MixerVoice.cs:23`
- Modify: `src/ARIA.Audio/MixerCommand.cs`
- Modify: `src/ARIA.Audio/AriaAudioEngine.cs:132-150`
- Test: `tests/ARIA.Audio.Tests/MixerBusTests.cs`

**Interfaces:**
- Consumes: `IAudioEngine.SetMarkers`, `StreamEvent.MarkerPosition`.
- Produces: `MixerBus.SetMarkers(StreamHandle handle, MarkerSpec[] markers)`;
  `CommandKind.SetMarkers`; `MixerCommand(..., MarkerSpec[]? Markers = null)`.

- [ ] **Step 1: Failing tests**

В `MixerBusTests`:
- `MarkerStop_StopsAtSecondMarker_AfterStartBetweenThem` — метки на 0.25с и
  0.5с, `StartFrame` на 0.4с, стоп на 0.5с (`MarkerPosition` = 0.5с).
- `MarkerStop_IgnoresMarkerAtStartPosition` — метка на 0.5с, старт на 0.5с,
  до конца буфера событий нет.
- `MarkerStop_RearmsMarkerAfterSeekBack` — стоп на метке 0.5с, `Seek` на 0.25с,
  снова стоп на 0.5с.
- `SetMarkers_AppliesToLiveVoice` — голос без меток, `SetMarkers` с меткой на
  0.25с, стоп на 0.25с.
Существующие `MarkerStop_FiresExactlyWhenPositionPasses_AndSilencesVoice` и
`Render_NoAllocations_InSteadyState` обязаны остаться зелёными.

- [ ] **Step 2: Run and see them fail**

`dotnet test tests/ARIA.Audio.Tests` → новые тесты падают (нет `SetMarkers`,
стоп только по первой метке).

- [ ] **Step 3: Implement**

`ScanCutoff` каждый блок считает локальный индекс: первая метка с
`Position > cueIn + startFrame / sampleRate`; в цикле по кадрам стоп на
`filePos >= Markers[index].Position` с передачей позиции в `EndVoice` →
`StreamEvent.MarkerPosition`. Состояние индекса не хранится. `SetMarkers`
сортирует массив у вызывающего (`AriaAudioEngine`, вне Render) и кладёт в
очередь; `DrainCommands` присваивает `voice.Markers`.

- [ ] **Step 4: Run tests**

`dotnet test tests/ARIA.Audio.Tests` → PASS, включая тест нулевых аллокаций.

- [ ] **Step 5: Commit**

```bash
git add src/ARIA.Audio tests/ARIA.Audio.Tests
git commit -m "feat: движок останавливается на любой метке и принимает метки на ходу"
```

---

### Task 4: ShowController — обработчики, флор, отмена, live re-arm

**Files:**
- Modify: `src/ARIA.Core/Runtime/ShowController.cs` (обработчики команд,
  `DeckInstance.MarkerFloor`, стек отмены, `Content`, re-arm)
- Test: `tests/ARIA.Core.Tests/MarkerCommandsTests.cs` (new),
  `tests/ARIA.Core.Tests/PlaybackTests.cs`

**Interfaces:**
- Consumes: команды Task 1, `SetMarkers` Task 3, `TestShow` хелперы.
- Produces: `SetTrackMarkers`/`SetEntryMarkers`/`Undo` в диспетчере команд;
  `DeckContent.Markers` заполнен; `MarkerFloor` в `StreamOptions.Markers`.

- [ ] **Step 1: Failing tests**

`MarkerCommandsTests` (фейк движка записывает вызовы, как в `PlaybackTests`):
- `SetTrackMarkers_UpdatesTrackAndEmitsShow`.
- `SetEntryMarkers_PreservesOtherOverrideFields` — у вхождения есть `Name`,
  после команды `Name` на месте, метки новые.
- `SetEntryMarkers_NullRemovesEmptyOverride` — вхождение без override, команда
  с `null` → `Overrides == null`; вхождение с одним `Name` и метками, команда с
  `null` → `Overrides.Name` сохранён, `Markers == null`.
- `SetTrackMarkers_RejectsInvalidMarkers` — пустое имя → отказ `marker-name-empty`,
  состояние не изменилось.
- `SetMarkers_ReachesPlayingTrack` — играет трек, команда меток → в движок ушёл
  `SetMarkers` с новым массивом; `EmitTransport` доехал.
- `Undo_RestoresPreviousMarkers` — поставить метку, `Undo` → прежний массив,
  повторный `Undo` → отказ `nothing-to-undo`.
- `Undo_IsNotRecordedTwice` — поставить, удалить, `Undo`, `Undo` → первая метка
  на месте, стек пуст.
- `Undo_RestoresNullOverride` — постановка первой метки на вхождение без
  override, `Undo` → `Overrides == null`.
- `MarkerFloor_SkipsFiredMarkerOnReplay` — стоп по метке 0.5с, Play → в
  `StreamOptions` метки с `Position > 0.5с`; `SeekTo` назад → метка снова в
  наборе.
- `DeckContent_CarriesMarkers` — `TransportState.Current.Markers` совпадает с
  эффективными метками.

- [ ] **Step 2: Run and see them fail**

`dotnet test tests/ARIA.Core.Tests` → обработчиков нет.

- [ ] **Step 3: Implement**

Обработчики по образцу `OnSetTrackAudio` (`ShowController.cs:1264`): валидация
→ замена в `_tracks`/`_projects` → `EmitShow()` → если правится играющий трек,
обновить `_current.Settings`, позвать `_engine.SetMarkers` и `EmitTransport()`.
Нормализация пустого override-рекорда — при записи вхождений. Стек отмены:
`List<Command>` до 64, инверсия пишется в обработчиках, `Undo` выполняет
команду без записи инверсии. `MarkerFloor` — поле `DeckInstance`, выставляется
в ветке `StoppedByMarker` из `StreamEvent.MarkerPosition`, сбрасывается в
`OnSeekTo`, `OnJumpTo`, `Replay`, `Stop`, смене вхождения; применяется при
сборке `StreamOptions` в трёх местах (`:743`, `:1173`, `:1968`) через общий
хелпер `ArmedMarkers(deck)`.

- [ ] **Step 4: Run tests**

`dotnet test tests/ARIA.Core.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ARIA.Core/Runtime/ShowController.cs tests/ARIA.Core.Tests
git commit -m "feat: команды меток, отмена и флор автостопа в ShowController"
```

---

### Task 5: Интерфейс — полоса меток, жесты, редактор, Ctrl+Z

**Files:**
- Modify: `src/ARIA.App/ViewModels/TransportViewModel.cs`
- Modify: `src/ARIA.App/Views/PlaybackHeader.axaml`, `PlaybackHeader.axaml.cs`
- Modify: `src/ARIA.App/Services/HotkeyConfig.cs`, `HotkeyCommands.cs`,
  `HotkeyLabels.cs`, `src/ARIA.App/ViewModels/Settings/HotkeysSectionVm.cs:12`
- Modify: `src/ARIA.App/Views/MainWindow.axaml.cs:291` (подавление хоткеев в
  текстовом вводе)
- Test: `tests/ARIA.App.Tests/MarkerLaneHeadlessTests.cs` (new)

**Interfaces:**
- Consumes: `DeckContent.Markers`, `SetEntryMarkers`/`SetTrackMarkers`/`Undo`.
- Produces: `TransportViewModel.Markers`, `.CurrentEntryId`, `.CurrentDuration`;
  `PlaceMarker(TimeSpan, string)`, `RenameMarker(Marker, string)`,
  `MoveMarker(Marker, TimeSpan)`, `RemoveMarker(Marker)`; хоткей `undo`.

- [ ] **Step 1: Failing tests**

`MarkerLaneHeadlessTests` (сессия из `HeadlessSessionFixture`, фейк-контроллер
из `PlaybackHeaderHeadlessTests`):
- `RightPress_OnEmptyWave_PlacesMarkerAndOpensEditor` — `DeckContent` с
  длительностью 90с, ПКМ на середине → в контроллер ушёл `SetEntryMarkers`
  (вхождение) или `SetTrackMarkers` (без вхождения) с меткой на 45с; на панели
  виден `TextBox` редактора.
- `EditorEnter_CommitsName`, `EditorEscape_DropsPlacedMarker`.
- `LeftPress_OnMarker_SeeksToItsPosition` — ушёл `SeekTo` с позицией метки.
- `RightPress_OnMarker_WithoutMove_RemovesIt`.
- `RightPress_OnMarker_WithMove_SendsMovedPosition`.
- `CtrlZ_InsideEditor_DoesNotSubmitUndo`, `CtrlZ_OutsideEditor_SubmitsUndo`.
- `CloseMarkers_DoNotDrawSecondName` — две метки ближе 28px → имён на панели
  одно (ищем `TextBlock` с именем второй).

- [ ] **Step 2: Run and see them fail**

`dotnet test tests/ARIA.App.Tests` → панель не знает меток.

- [ ] **Step 3: Implement**

`PlaybackHeader.axaml`: `Grid RowDefinitions="14,56,14"` — время, волна, имя;
поверх — `Canvas` меток и `Canvas` с одним `TextBox` редактора. В code-behind:
`DrawMarkers()` на смену `Markers`/размер, `MarkerAt(x)`, ветвление в
`OnWavePressed` по кнопке и попаданию, порог 3px, редактор с автфокусом через
`Dispatcher.UIThread.Post`. VM: `Markers`/`CurrentEntryId`/`CurrentDuration` из
`Apply(TransportState)`, четыре метода собирают новый массив (вставка по
позиции, сортировка) и шлют команду. Хоткей `Ctrl+Z` → `undo` → `new Undo()`
плюс строка в `HotkeyLabels` и в списке `HotkeysSectionVm`. В `OnKeyDown`
перед `_hotkeys.TryHandle` добавить `!IsTextInput(e.Source)`.

- [ ] **Step 4: Run tests**

`dotnet test tests/ARIA.App.Tests` → PASS. Затем `dotnet test` по солюшену.

- [ ] **Step 5: Commit**

```bash
git add src/ARIA.App tests/ARIA.App.Tests
git commit -m "feat: полоса меток на волне — постановка, правка, удаление, Ctrl+Z"
```

---

### Task 6: Документы

**Files:**
- Modify: `DESIGN.md` (Surfaces/PlaybackHeader)
- Modify: `CONTEXT.md` (Marker — переопределяется на вхождении)
- Create: `docs/adr/0010-marker-arming-and-command-undo.md`
- Modify: `docs/superpowers/specs/2026-10-12-markers-seek-panel-design.md` —
  отметить, что реализовано

**Interfaces:** нет кода.

- [ ] **Step 1: Написать**

`DESIGN.md`: в Surfaces/PlaybackHeader добавить полосу меток (14+56+14),
цвет `BrushDim`/`BrushFg` на наведении, линия 1px + ромб 7×7, скрытие имени
при скученности <28px. `CONTEXT.md`: у `Marker` добавить «переопределяется на
PlaylistEntry». ADR 0010: почему метка в точке старта считается пройденной,
зачем `MarkerFloor`, почему отмена сделана обратными командами и живёт в
сессии.

- [ ] **Step 2: Commit**

```bash
git add DESIGN.md CONTEXT.md docs
git commit -m "docs: полоса меток в DESIGN.md, метки вхождения в CONTEXT.md, ADR 0010"
```

---

## Проверка среза

- `dotnet build` — 0 предупреждений.
- `dotnet test` по солюшену — зелёный.
- `dotnet run --project tools/Snapshots -- <dir>` — рендер MainWindow; метки
  не меняют раскладку, янтарь остаётся только у playhead'а.
- `impeccable detect --json src/ARIA.App/Views/PlaybackHeader.axaml` — один
  прогон после готовности интерфейса.
