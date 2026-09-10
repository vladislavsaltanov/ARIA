# Аудит UI-кода и Show-состояния: что живо, куда ляжет сценарий

Type: research
Status: resolved

## Question

Инвентаризация текущего UI-слоя и состояния Show, чтобы тикеты компоновки и сценария опирались на факты, а не догадки:

- Что из ViewModels реально привязано к окну, что — мёртвый код: `QueueViewModel` не отображается ни в одном табе? `TransportViewModel.NextName` нигде не показан? `Locked` — только локальное свойство VM без команды в `ICommandBus`?
- `HotkeyService`/`HotkeyConfig`: как зарегистрированы жесты, куда идут, редактируются ли, видны ли пользователю?
- `RemotePanel`: что в нём (pairing, QR?) и что из этого конфигурация, а не шоу-режим.
- `ISnapshotStore`/версионированный state: как устроена схема Show, как добавляются новые части состояния — куда ляжет текст сценария, если он часть Show?
- Waveform-кэш Track: где живёт, кто читает, есть ли хоть один рендер волны в UI?
- Ограничения: тест на отсутствие аллокаций в Render/аудио-callback (`GC.GetAllocatedBytesForCurrentThread`) — где лежит и что именно проверяет?

Ответ: карта «живое/мёртвое/куда ляжет новое» с файлами и строками.

## Answer

Живое: TransportViewModel (DataContext окна, но NextName нигде не показан), PlaylistsView/LibraryView/RemotePanel. Мёртвое: QueueViewModel (не создаётся никем, кроме тестов), Locked во всех трёх VM — локальные флаги без команды в ICommandBus (в Commands.cs/CommandCodec Lock нет), AppHost.ImportTracksAsync — дубль живого LibraryViewModel.ImportAsync. Hotkeys — оконный перехват, конфиг hotkeys.json, on-the-fly реконфига и видимости пользователю нет. RemotePanel — чистый pairing (QR, URL, идентификатор/пароль, mDNS), шоу-режима нет. State: 4 партиции с монотонными версиями; ShowDocument без версии схемы, новые поля добавляются опционально — миграция не нужна (битое = чистый старт); текст сценария на entry уже существует как PlaylistOverrides.Note (персистится, умеется remote), уровень Playlist/Show придётся добавлять (Commands → ShowController → StateEvents → SnapshotStore → ShowAutosaver → AppHost). Waveform-кэш: waveforms.db (25 pts/s, float-пары), пишет импорт, читателей в проде нет, рендера волны в UI ноль. Аллокационные тесты: только аудио — MixerBusTests.cs:177 (Render) и DspNodeTests.cs:76 (DSP-пайплайн), UI-поток не покрыт. Композиция: App.Compose на UI-потоке, VM'ы подписаны на CommandBus (Pumped, Control-поток = pump-таск); Transport/RemotePanel маршаллят через SynchronizationContext, Playlists/Library/Queue — мутируют коллекции с pump-потока.

Полный отчёт: [research/02-ui-state.md](../research/02-ui-state.md)
