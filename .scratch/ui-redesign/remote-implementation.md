# Remote-пульт: план реализации (spec.md §9)

Документ для агентов. Сервер (`ARIA.Remote`) готов почти целиком — работа почти вся в клиенте
(`src/ARIA.Remote/RemoteClient/`). Правила репо — AGENTS.md. Домен — CONTEXT.md. Спека — spec.md §9.

## Приёмка целиком

Пульт в браузере показывает: транспорт-бар (play/pause/stop/next/replay, «играющий + далее»,
прогресс), PANIC, мастер-громкость (слайдер + mute), LUFS (число + полоска), таймеры
(show clock + elapsed трека), очередь (просмотр, reorder, удаление, clear), плейлисты
(просмотр, активный, перемещение/добавление/удаление, overrides), сценарий только-чтение
со follow-подсветкой и смарт-кликом, индикатор LOCK. При локе шина отвергает команды пульта
(Rejected), PANIC проходит.

## Что уже есть (не делать заново)

- Протокол: `/auth` (идентификатор+пароль → token), `/ws?token=` WebSocket.
  Вход: `snapshot` {Show, Transport, Queue, Mixer}, `delta {Partition, Version, State}`,
  `position` (latest-value, 10 Гц), `lufs` (10 Гц). Исход: `{client, seq, command:{type,...}}`,
  ответы `ack`/`rejected`. Кодек — `CommandCodec.cs`.
- Команды в кодеке, UI только их рисует: `play pause stop next replay panic clear_queue
  jump_to enqueue_entry enqueue_track remove_from_queue move_queue_item create_playlist
  rename_playlist delete_playlist set_active_playlist add_entry remove_entry move_entry
  set_entry_overrides set_master_gain set_muted`. Новых команд НЕТ (spec §9).
- Лок: `ShowController` отвергает команды при `Locked`, кроме Panic/SetLocked/TickShowClock.
- State-поля (StateEvents.cs): `ShowState{Playlists, ActiveId, Locked, Clock, Scripts, TrackDigest}`,
  `ShowClockState{Elapsed, Running}`, `MixerState{MasterGainDb, Muted, PanicFade}`,
  `TransportState{Status, Current, Next, Faulted}` (DeckContent: DisplayName, Duration, CueIn, CueOut),
  `QueueState{Items}` (QueueItem: DisplayName, Color), `Script{Id, Name, Lines}`,
  `ScriptLine{Id, AtElapsed, Text, Mentions}`, `Mention{Track}`, `TrackDigest{Entries: TrackId→DisplayName}`.
- Клиент: логин, сохранение пары в localStorage, транспорт, PANIC с подтверждением,
  просмотр очереди, dedupe ack (`pendingAck`), переподключение.
- Сервинг: клиент — embedded-ресурсы (`Aria.Remote.RemoteClient.*`), добавлять файлы = править
  ARIA.Remote.csproj (EmbeddedResource). Нет бандлера, нет зависимостей — ванильный JS/CSS.

## Таймеры — сервер не трогать

Show clock уже внутри Show-партиции (`Clock`), приедет в snapshot/delta. Время суток — клиентские
часы. Elapsed трека — из `position`-кадра. Работы на клиенте: слот таймеров в шапке, `м:сс`.

## Срезы (каждый — зелёный `dotnet test`, 0 предупреждений, один коммит)

1. **Микшер + LUFS + таймеры.** Слайдер 0–100% → −80…+12 дБ → `set_master_gain`;
   кнопка mute → `set_muted`; движение слайдера снимает mute (логика клиента, spec §4).
   LUFS: число + полоска из `lufs`-кадра. Слот таймеров (show clock крупным, elapsed мелким).
   Тест: frame-shape `lufs`/`position` уже покрыт Remote-тестами — здесь только UI.
2. **Очередь: действия.** Кнопки/свайпы: удалить (`remove_from_queue`), reorder
   (`move_queue_item`), Clear в шапке (`clear_queue`). Подсветка играющего (сверка с
   Transport.Current). Отрисовка — уже есть, дополнить.
3. **Плейлисты.** Список плейлистов из Show-партиции; активный помечен (`ActiveId`);
   перемещение трека (`move_entry`), добавление (`add_entry`), удаление (`remove_entry`),
   overrides (`set_entry_overrides`). Имена треков — TrackDigest. Без создания/переименования
   плейлистов? НЕТ — команды в кодеке, включить (`create_playlist`, `rename_playlist`,
   `delete_playlist`, `set_active_playlist`).
4. **Сценарий только-чтение.** Вкладки = `Scripts`; строки: AtElapsed моно + Text + чипы
   (Mention → имя из TrackDigest, повисшее — серым); follow-подсветка: строка с
   AtElapsed ≤ Clock.Elapsed < следующего; смарт-клик: 1 Mention → `enqueue_track`,
   несколько → выбор, 0 → ничего.
5. **LOCK-индикатор.** `Locked` из Show → плашка «LOCKED» на пульте. Пульт не шлёт
   SetLocked. Проверить rejected-путь: при локе любая кнопка кроме PANIC получает `rejected`.

Клиентские файлы: `index.html`, `app.js`, `app.css` (+ при надобности новые .js, все —
embedded-ресурсы). Серверный код не менять, кроме случая, когда тест покажет дыру в кадрах.

## Вне объёма

Preview PFL (движок не реализует), seek с пульта (вне спеки §9; отдельное решение),
библиотека/импорт/настройки/waveform на пульте (desktop-only по spec §9).

## Verify

```sh
dotnet test tests/ARIA.Remote.Tests   # 43+ зелёных, кадры/кодек не сломаны
dotnet test                            # весь солюшен зелёный, 0 предупреждений
grep -c 'set_muted\|move_queue_item\|enqueue_track' src/ARIA.Remote/RemoteClient/app.js  # ≥3 после срезов 1–2
```

Ручной приём (spec §9 критерий): два устройства в LAN — пульт видит «играющий + далее»,
двигает громкость, видит LUFS, follow идёт за show clock, при локе кнопки дают rejected,
PANIC глушит всё.
