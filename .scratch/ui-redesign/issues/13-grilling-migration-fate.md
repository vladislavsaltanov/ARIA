# Миграция: судьба текущих вьюх и ViewModel

Type: grilling
Status: resolved
Blocked by: 06, 07, 09, 10

## Question

Схема новой компоновки решена (рейка + бар + drawer, см. тикет 05). Решить, что происходит с существующим кодом при переходе — это раздел спеки, не реализация:

- QueueViewModel (мёртв, никем не создаётся) — возрождается как VM очереди в рейке или удаляется с написанием нового?
- RemotePanel — что переносится в диалог настроек дословно, что выбрасывается; судьба RemotePanelViewModel.
- Дубликат импорта (AppHost.ImportTracksAsync vs LibraryViewModel.ImportAsync) — какой путь канонизируется.
- PlaylistsView/LibraryView: что из их VM-логики переиспользуется в новой рейке/центре, что переписывается; «SET NAME/RENAME»-путаница исчезает вместе с вью?
- Порядок внедрения: что меняем первым (бар? рейка? drawer?) чтобы рабочее приложение оставалось зелёным после каждого шага; что из старого удаляется на каждом шаге.

Ответ: таблица «файл → судьба» + порядок шагов внедрения для спеки.

## Answer

Решение пользователя (2026-09-11), пять развилок HITL — все рекомендованные. Сверено с кодом: QueueViewModel цела и покрыта тестами (создаётся только тестами), AppHost.ImportTracksAsync никем не вызывается, RemotePanelViewModel отвязана от Show, ToggleLockCommand живёт в TransportViewModel и App.axaml.cs.

**Таблица «файл → судьба»**:

- `ViewModels/QueueViewModel.cs` — **возрождается** VM колонки очереди: остаются Rebuild/MarkCurrent/Selected/ClearQueue/RemoveSelected + существующие тесты; удаляются MoveUp/MoveDown (заменены drag, тикет 10) и локальный Locked (лок — команда Show, тикет 05); вью пишется заново вокруг неё.
- `ViewModels/RemotePanelViewModel.cs` — **дословно** DataContext'ом секции «Пульт» диалога настроек; `Views/RemotePanel.axaml` перерисовывается под диалог (QR/URL/пара/сброс/mDNS без урезаний).
- Импорт: канонический движок — **`AppHost.ImportTracksAsync`** (батч, MergeTracks, IProgress, возврат треков); `LibraryViewModel.ImportAsync`/`Upsert`/`SyncShowState` (RestoreShow-путь) умирают, VM худеет до пикера + прогресс + отчёт; движок дописывается до отчёта «добавлено N, пропущено M» (тикет 10 — нет ни в одном пути); drag&drop использует тот же движок.
- `ViewModels/PlaylistsViewModel.cs` — **живёт**: CRUD плейлистов, RemoveEntry, Rebuild с резолвом имён, селекции, EnqueueTrack-логика (на double-click); умирают MoveEntryUp/Down, AddToActive, SetEntryName/ClearEntryOverrides (без UI), локальный Locked. Команда SetActivePlaylist остаётся только для пульта; выбор плейлиста на десктопе — чистое UI-состояние (тикет 10: активного плейлиста как состояния нет). Команда шины SetEntryOverrides остаётся (совместимость пульта).
- `ViewModels/LibraryViewModel.cs` — **живёт**: пикер, Tracks/Reload, EnqueueTrack, селекция; умирает AddToActive.
- `ViewModels/TransportViewModel.cs` — **живёт и расширяется** (NextName, громкость из MixerState, LUFS, таймеры, замок-индикатор — тикет 06); умирают ToggleLockCommand и локальный Locked (тикет 05).
- `Views/MainWindow.axaml` (табы), `PlaylistsView`, `LibraryView` — удаляются по шагам; SET NAME/RENAME-путаница умирает вместе с ними (переименование плейлиста — только inline, тикет 10).

**Порядок шагов** (каждый — зелёный билд, поведение не ломается):

1. Движок + команды Show: сим публикации метрик, тап + K-weighting, Muted-флаг (06); Lock-команда + Locked-флаг + show clock elapsed/running (05/08) в Show-state. UI не меняется.
2. Транспорт-бар 76px: новое вью + расширение TransportViewModel; умирают кнопка LOCK и ToggleLockCommand. Табы живы.
3. Рейка + центр + очередь: новые вью; QueueViewModel возрождается; VM худеют; табы SHOW/LIBRARY умирают; мёртвые команды удаляются.
4. Drawer сценария: Script + TrackDigest в Show-state (штатный путь Commands → ShowController → StateEvents → SnapshotStore → ShowAutosaver, без миграции — research/02); панель сценария.
5. Диалог настроек: переезд RemotePanel, редактор жестов (09), сброс часов; таб REMOTE умирает; старые вью удалены полностью.
