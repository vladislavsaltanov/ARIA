# Ресерч 02: аудит UI-слоя и Show-состояния

Закрывает тикет [02-research-ui-state-audit.md](../issues/02-research-ui-state-audit.md). Все ссылки — `файл:строка` по состоянию HEAD.

## 1. Живое/мёртвое в UI-слое

### Кто вообще создаётся в рантайме

Всё собирается руками в `App.Compose` (src/ARIA.App/App.axaml.cs:56-91), без DI-контейнера:

- `TransportViewModel(bus, monitor, sync)` — App.axaml.cs:63
- `PlaylistsViewModel(bus, trackSource)` — App.axaml.cs:64
- `LibraryViewModel(bus, library, waveforms, importer, () => desktop.MainWindow)` — App.axaml.cs:65
- `RemotePanelViewModel(sync)` — App.axaml.cs:66
- `HotkeyService` — App.axaml.cs:67-69
- Окно: `new MainWindow(hotkeys, playlists, remote, library) { DataContext = transport }` — App.axaml.cs:70

### Живое (привязано к окну)

DataContext окна = `TransportViewModel` (App.axaml.cs:70), вкладкам DataContext назначается в конструкторе окна (src/ARIA.App/Views/MainWindow.axaml.cs:22-33: ShowTab→Playlists, RemoteTab→RemotePanel, LibraryTab→Library).

Биндинги окна (src/ARIA.App/Views/MainWindow.axaml):

- `StatusText` — :13 (строка «STOP/PLAY/PAUSE/PANIC»)
- бейдж `LOCKED` по `Locked` — :14
- вкладка TRANSPORT: `DisplayName` — :20, `Remaining` — :21, команды PLAY/PAUSE/STOP/NEXT/REPLAY/PANIC — :24-31, LOCK/UNLOCK-кнопка по `Locked` — :32-37
- вкладки SHOW (`PlaylistsView.axaml`) и LIBRARY (`LibraryView.axaml`) — живут, привязаны к своим VM.

RemotePanel — жив, это вкладка REMOTE (см. §3).

### Мёртвое / полумёртвое

- **`QueueViewModel` — полностью мёртв в продакшене.** Никакой view его не использует и никто не создаёт: единственные упоминания — сам класс (src/ARIA.App/ViewModels/QueueViewModel.cs:12) и тесты (tests/ARIA.App.Tests/QueueViewModelTests.cs:30,40,52,70,82,95). Класс рабочий (подписка на `QueueDelta`/`TransportDelta`, команды ClearQueue/RemoveFromQueue/MoveQueueItem — QueueViewModel.cs:35-72), но в окне вкладки Queue нет (MainWindow.axaml:17-50 — только TRANSPORT/SHOW/LIBRARY/REMOTE). Для нового UI это готовая логика, которую можно переиспользовать, а не переписывать с нуля.
- **`TransportViewModel.NextName` — не показывается нигде.** Свойство есть (TransportViewModel.cs:30), обновляется на каждом `TransportDelta` (TransportViewModel.cs:126-127), но ни один AXAML к нему не привязан (grep по всем .axaml — 0 попаданий). Данные для «СЛЕДУЮЩИЙ» в новом транспорте уже текут.
- **`TransportViewModel.Locked` — только локальный флаг VM.** `ToggleLock` лишь инвертирует свойство (TransportViewModel.cs:74-75); ворота `CanPlay…CanReplay` (TransportViewModel.cs:77-85) и уведомление команд (TransportViewModel.cs:87-94) — локальные. В `Aria.Core/Commands/Commands.cs` (:11-68) команды Lock нет; в remote-кодеке её тоже нет (src/ARIA.Remote/CommandCodec.cs:47-72 — play…set_panic_fade). Хоткей `Ctrl+L`→`lock` ведёт в ту же VM-команду (App.axaml.cs:103). Итог: блокировка не видна пульту, не сохраняется, чисто десктопная.
- **`Locked` в `PlaylistsViewModel` и `QueueViewModel` — всегда false.** Объявлены (PlaylistsViewModel.cs:20-21, QueueViewModel.cs:19-20), ничего их не меняет; ворота `CanEdit` всегда открыты.
- **Дублированный импорт: два параллельных пути.** `AppHost.ImportTracksAsync` (AppHost.cs:110-147; пишет peaks, библиотеку, шлёт `MergeTracks`) вызывается только из тестов (tests/ARIA.App.Tests/AppHostTests.cs:82,91). Реальный UI-импорт идёт через `LibraryViewModel.ImportAsync` (LibraryViewModel.cs:81-124; пишет peaks :105-108, библиотеку :161-172, шлёт `RestoreShow` :174-184). При редизайне выбрать один путь.
- **Мелочь/баг в `PlaylistsView`:** кнопка RENAME берёт текст из `EntryNameBox`, а не `PlaylistNameBox` (PlaylistsView.axaml:50, `CommandParameter={Binding #EntryNameBox.Text}`); поле `PlaylistNameBox` (:49) не используется нигде. `Locked` окна и вкладок не синхронизированы между VM — три независимых флага.

## 2. HotkeyService / HotkeyConfig

- **Конфиг:** `HotkeyBinding(Gesture, Action)` (src/ARIA.App/Services/HotkeyConfig.cs:6); дефолт :17-25 — Space→play, Escape→pause, Ctrl+Shift+P→panic, Ctrl+N→next, Ctrl+R→replay, Ctrl+L→lock. `Load(path)` :27-46 (нет/битый файл → Default), `Save(path)` :48-57 существует, но **никто не вызывает** (кроме тестов). Файл: `{dataDir}/hotkeys.json`, путь передаётся в App.axaml.cs:68.
- **Сервис:** `HotkeyService` (src/ARIA.App/Services/HotkeyService.cs) — в конструкторе (:8-20) строит словарь «нормализованный жест → действие» (первая запись побеждает дубликат, :15); `TryHandle(gesture)` :22-31 диспатчит строку действия в делегат. Нормализация :33-57 — регистр не важен, порядок модификаторов канонизируется (ctrl<alt<shift<meta).
- **Регистрация:** не OS-глобальные хоткеи, а оконный перехват: туннелирующий `KeyDown` на Window (MainWindow.axaml.cs:40), жест строится из `Key`/`KeyModifiers` (GestureKey :55-71 — цифры/numpad/space/escape, BuildGesture :73-93) и отдаётся `TryHandle`; съеденное событие помечается `e.Handled` (:50-52). Вне фокуса окна хоткеи не работают.
- **Куда диспатчатся:** `App.DispatchHotkey` (App.axaml.cs:93-105) маппит строки-действия на `RelayCommand` **TransportViewModel** (play/pause/stop/next/replay/panic/lock) — то есть через VM-команды, а не напрямую в `ICommandBus`. Команды внутри VM уже шлют в шину (TransportViewModel.cs:56-72, Submit :102).
- **Переконфигурация на лету:** нет — у `HotkeyService` нет API мутаций, словарь строится один раз в конструкторе; конфиг перечитывается только при старте.
- **Видимость пользователю:** нулевая — ни один AXAML не показывает биндинги, редактора нет.

## 3. RemotePanel + RemotePanelViewModel

Всё, что в панели, — **конфигурация (pairing), никакого шоу-режима**:

- **VM** (src/ARIA.App/ViewModels/RemotePanelViewModel.cs): `urlText` :15-16, `qrImage` :18-19, `mdnsStatus` :21-22, `identifierText` :24-25, `passwordText` :27-28, `hasInfo` :30-31, единственная команда `ResetPasswordCommand` :33. Методы: `Configure` :41-47, `ResetPassword` :51-61, `SetMdns` :63-64. Статуса подключённых клиентов/сессий в VM нет — только анонс.
- **View** (src/ARIA.App/Views/RemotePanel.axaml): QR :9-11, URL :14-15, идентификатор :16-19, пароль :20-23, кнопка «новый пароль» :24-26, подсказка про пересканирование :27-28, статус mDNS :29-30.
- **Сборка:** App.axaml.cs:74-90 — `RemoteAnnouncer.Announce()` строит URL+QR (`RemoteAnnouncer.BuildInfo`, src/ARIA.App/Services/RemoteAnnouncer.cs:120-127, QRCode→PNG), `remote.Configure(...)` с колбэком сброса пароля (:83-88), `SetMdns(announcer.Start())` :89 (mDNS-анонс `_aria._tcp`, RemoteAnnouncer.cs:56-62).
- **Pairing-хранилище:** `RemoteCredentialsStore` (src/ARIA.Remote/RemoteCredentialsStore.cs) — `remote-auth.json`, стабильный identifier + пароль, `ResetPassword` :59-64. CLI: `--remote-id / --remote-set-password / --remote-reset-password` (src/ARIA.App/Services/RemoteCredentialCli.cs:7-37, вход из Program.cs:18-25).
- **Сервер:** `RemoteHost` (src/ARIA.Remote/RemoteHost.cs) — `RemoteOptions(AuthToken, Port, Credentials, BindAddress)` :21; маршруты /health /auth /ws / :77-81; WS пушит snapshot/delta/position клиенту (src/ARIA.Remote/RemoteClient/app.js:130-133). Панель в окне — только витрина pairing-данных; сами сессии живут в RemoteHost и в UI не видны.

## 4. ISnapshotStore / версионированный state Show

- **Партиции:** Show/Transport/Queue/Mixer (src/ARIA.Core/State/StateEvents.cs:7-13). У каждой свой монотонный счётчик версии (ShowController.cs:37-40), события — полные слепки партиции без диффов: `ShowDelta/TransportDelta/QueueDelta/MixerDelta` (StateEvents.cs:52-58), агрегат `ShowSnapshot` :62-70. Show-партиция = `PlaylistsState(Playlists, ActiveId)` :46.
- **Документ:** `ShowDocument(Tracks, Playlists, ActiveId, Queue, MasterGainDb, PanicFade, SavedAt)` (src/ARIA.Persistence/SnapshotStore.cs:8-15). **Поля версии схемы нет.** `JsonSnapshotStore` пишет атомарно (.tmp+Move :36-38), `LoadLatest` глотает JsonException/IOException → null :53-56 (битый файл = чистый старт). DTO :81-111; мапперы src/ARIA.Persistence/Mappers.cs (Track :69-109, Playlist :111-168).
- **Автосейв:** `ShowAutosaver` (src/ARIA.Persistence/ShowAutosaver.cs) подписан на ShowDelta/QueueDelta/MixerDelta (:47), debounce 500 мс (AppHost.cs:91); сохранение берёт `bus.Snapshot()` :64-76, а треки — из `SqliteLibraryStore` (треки в Show-партиции не живут). Восстановление: AppHost.cs:93-97 шлёт `RestoreShow`.
- **Как добавить новую часть состояния (текст сценария):**
  1. **Если это текст на PlaylistEntry — он уже есть:** `PlaylistOverrides.Note` (src/ARIA.Core/Model/Playlist.cs:13), персистится (Mappers.cs:44,120,137), remote-кодек умеет (CommandCodec.cs:120), UI показывает read-only (PlaylistsView.axaml:33), редактирование — через существующий `SetEntryOverrides`. До сценария в полном смысле не хватает: UI-редактора и, возможно, текста уровня Playlist/Show.
  2. **Если это новая сущность уровня Show** (строки/блоки сценария): трогаются — `Commands.cs` (новая команда + расширение `RestoreShow`/`LoadShow`), `ShowController.cs` (поле, счётчик версии, emit, ветка Handle :58-141, Snapshot :143-151, валидация по образцу :292-321), `StateEvents.cs` (новая Delta/State), `SnapshotStore.cs` (ShowDocument + ShowDocumentDto + ToDto/ToDomain), `ShowAutosaver.cs` (:64-76 — включить в документ), `AppHost.cs:96` (аргумент RestoreShow), тесты `RestoreShowTests`/`SnapshotStoreTests`.
  3. **Миграция не нужна:** схема не версионирована, новые поля добавляются опциональными (null-able в DTO, missing member → default); старые show.json читаются как раньше, несовместимое/битое — молча превращается в чистый старт (SnapshotStore.cs:53-56). Это осознанный трейд-офф — ломающие изменения схемы сейчас невозможны без ручной чистки файла.

## 5. Waveform-кэш Track

- **Модель:** `WaveformPeaks(TrackId, PointsPerSecond, SampleRate, ImmutableArray<PeakPoint>)`, `PeakPoint(Min, Max)` (src/ARIA.Core/Model/WaveformPeaks.cs:5-10).
- **Производство:** при импорте — `TrackImporter.Import` (src/ARIA.App/Services/TrackImporter.cs:21-50, 25 точек/сек :21) через `WaveformScanner.Scan` (src/ARIA.Audio/WaveformScanner.cs:17-72, моно-микс, min/max по бакетам).
- **Хранение:** `SqliteWaveformStore` (src/ARIA.Persistence/WaveformStore.cs:14-93) — отдельный `waveforms.db`, таблица `waveform(track_id PK, points_per_second, sample_rate, points BLOB)` :86-92, точки — пары float32 (8 байт) :16,102-126. Инвариант: peaks не в шоуре, а по TrackId.
- **Кто пишет в проде:** `LibraryViewModel.ImportAsync` :105-108 (и мёртвый дубль AppHost.ImportTracksAsync :135-138).
- **Кто читает:** **никто в проде.** `IWaveformStore.Load` вызывается только в тестах (tests/ARIA.App.Tests/AppHostTests.cs:89, tests/ARIA.Persistence.Tests/WaveformStoreTests.cs). `LibraryViewModel` держит стор (LibraryViewModel.cs:25), но только пишет.
- **Рендер волны в UI:** **нулевый.** Ни одного контрола/кастомного draw; в библиотеке показываются только имя+длительность (LibraryView.axaml:16-22). Данные готовы, потребитель отсутствует.

## 6. Тесты на аллокации

Тестов с `GC.GetAllocatedBytesForCurrentThread` ровно два, оба в tests/ARIA.Audio.Tests, оба про **аудио-рендер**, ни одного про UI:

- `MixerBusTests.Render_NoAllocations_InSteadyState` (tests/ARIA.Audio.Tests/MixerBusTests.cs:177-197): 200 разогревочных + 1000 измеренных `MixerBus.Render` — дельта аллокаций должна быть 0. Это ровно тот путь, который крутит render-поток (`AriaAudioEngine.RenderLoop`, src/ARIA.Audio/AriaAudioEngine.cs:102-116: faults → Render → master gain → sink → publish position).
- `DspNodeTests.Pipeline_NoAllocations_InSteadyState` (tests/ARIA.Audio.Tests/DspNodeTests.cs:76-98): SineSource→FaderNode→GainNode→MeterNode, то же требование 0 байт.

Ниже лежит «ноль локов» — в `MixerBus` рендер-путь без локов (проверяется и кооперацией потоков, MixerBusTests.cs:200+). Для нового UI ограничения такие: всё, что попадает в render-поток аудио (например, если волна/метр будут рисоваться из данных микшера), обязано быть 0-alloc/0-lock; сам Avalonia-UI слой тестом на аллокации не покрыт (инвариант AGENTS.md:39 относится к аудио-callback/Render, не к UI-потоку). Метрики для LUFS-метра уже существуют: `MeterNode.Peak/Rms/RunningPeak` (src/ARIA.Audio/DspNodes.cs:89-116), `MixerBus.Peak` (src/ARIA.Audio/MixerBus.cs:80) — но наружу движок их сейчас не отдаёт (IAudioEngine без метода метра).

## 7. AppHost.cs / MainWindow.axaml.cs: порядок композиции

- `Program.Main` (src/ARIA.App/Program.cs:12-28): `--selftest` → RunSelfTestAsync; `--remote-*` → RemoteCredentialCli; иначе Avalonia classic lifetime.
- `App.OnFrameworkInitializationCompleted` (App.axaml.cs:21-43): строит `AppHost(dataDir, remoteOptions)`; ShutdownRequested → dispose ≤3 с (:35-39); `ComposeAsync` файром :40.
- `ComposeAsync` (:45-54): `await host.StartAsync()` (AppHost.cs:66-104: движок, сторы, шина Pumped, `RestoreShow` из show.json :93-97, старт RemoteHost), затем `Dispatcher.UIThread.InvokeAsync(Compose)` — окно собирается строго на UI-потоке.
- `Compose` (:56-91): `sync = SynchronizationContext.Current` (:62) — это UI-контекст; VM'ы (:63-66); `HotkeyService` (:67-69); окно с `DataContext = transport` (:70); `Show()` :72; анонсер+RemotePanel.Configure :74-90.
- **Владелец и потоки:** состояние Show мутирует только на Control-потоке — это dedicated pump-таск `CommandBus` (src/ARIA.Core/Runtime/CommandBus.cs:115-147, LongRunning; ShowController вызывается только там). Подписчики шины получают события синхронно на pump-потоке (Publish :102-113). VM'ы делятся на два сорта:
  - потокобезопасные, маршаллят в UI через `SynchronizationContext.Post`: `TransportViewModel` (TransportViewModel.cs:104-114) и `RemotePanelViewModel` (RemotePanelViewModel.cs:82-92);
  - **немаршаллящие**: `PlaylistsViewModel` (Rebuild на pump-потоке, PlaylistsViewModel.cs:35-42,162-188), `LibraryViewModel` (Reload :186-196), `QueueViewModel` (:80-115) — мутируют `ObservableCollection` прямо с pump-потока. Для нового UI это шаблон, который надо фиксить/осознанно повторять.
- Позиция — вне state: `PlaybackMonitor` (latest-value, AppHost.cs:36) питается из render-потока (`PublishPosition`, AriaAudioEngine.cs:139-161), VM читает её подписками `Changed/Cleared` (TransportViewModel.cs:136-158).
- Порядок disposals: `AppHost.DisposeAsync` (AppHost.cs:154-178) — autosaver flush → remote → bus → engine → snapshots → library → waveforms.
- Замена компоновки: новая разметка входит через `Compose` (App.axaml.cs:56-91) + `MainWindow` (DataContext = Transport, вкладкам — свои VM); точки расширения — конструктор MainWindow (MainWindow.axaml.cs:18-35) и сам Compose.

## Куда ляжет новое (сводка)

| Новое из спеки | Фактическая база | Что отсутствует |
|---|---|---|
| Транспорт-бар | TransportViewModel (жив, DataContext окна), `NextName` уже течёт, `PlaybackMonitor` для elapsed | показ «следующего», таймеры шоу/суток |
| Сценарий | `PlaylistOverrides.Note` уже персистится и умеется remote'ом; Queue-логика в мёртвом QueueViewModel | UI-редактор Note, текст уровня Playlist/Show (→ §4), запуск «клик по строке → Queue» (команды Enqueue/JumpTo есть: Commands.cs:23-27) |
| Волна | peaks в waveforms.db готовы, 25 pts/s | рендер-контрол и читатель Load; UI-слой не покрыт аллокационным тестом |
| LUFS/метры | MeterNode/Peak в DSP | экспposition метра через IAudioEngine наружу |
| Громкость | `SetMasterGain` + MixerState персистится | UI-слайдер (команда есть, CommandCodec.cs:69) |
| Хоткеи | HotkeyService/Config работают, файл есть | on-the-fly реконфиг, редактор, показ пользователю |
| Пополнение библиотеки | LibraryViewModel.ImportAsync жив | унификация с мёртвым AppHost.ImportTracksAsync |
| Remote-конфиг | RemotePanel = чистый pairing | статус подключений в UI (живёт в RemoteHost, в VM не выведен) |
