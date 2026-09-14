# ARIA

> Кроссплатформенный аудиоплеер для звукорежиссёров мероприятий: шоу не должно остановиться.

ARIA — десктопный плеер (театр, концерты, конференции): мгновенный отклик, отказоустойчивость, дистанционное управление из браузера по локальной сети. Один Deck «текущий + следующий», кроссфейд внутри микшера, сценарии-ранлисты с упоминаниями треков, PANIC-глушение всего одним жестом.

## Возможности

- Deck: Play / Pause / Stop / Next / Replay с кроссфейдом, SeekFade при ручном seek.
- Queue поверх плейлиста: сначала Queue, дальше Playlist с текущей позиции.
- EndAction на вхождение: пауза, стоп, рестарт, следующий с кроссфейдом.
- CuePoint in/out, Marker с автостопом, Preview неиграющего трека на канале прослушки.
- PANIC: глушит зал и прослушку фейдом ≤100 мс, восстановление только вручную.
- Script-панель: строки свободного текста со временем от старта Show и Mention-ами треков; исполнение строки ставит трек в Queue; follow-подсветка по Show clock.
- Remote-пульт: браузер по LAN, HTTP + WebSocket, pairing «идентификатор + пароль», QR, mDNS-анонс.
- Библиотека SQLite, waveform-кэш, автосейв Show каждые 500 мс, восстановление после сбоя.
- Форматы: WAV, FLAC, MP3, OGG. Движок: 48 кГц, стерео, блок 512 фреймов.

## Требования

- **SDK для сборки**: .NET 10.0.401+ (`dotnet --list-sdks`).
- **Рантайм для запуска собранного**: не нужен — паблиш self-contained.
- **ОС сборки**: macOS (arm64-хост) собирает все 4 RID; нативный шим для linux/win — кросс-компиляторами (см. таблицу).
- **Тулинг нативного шима**: `cc` + Xcode CLT (macOS), `zig` (linux-x64), `mingw-w64` (win-x64). Установка: `brew install zig mingw-w64`.

## Установка из готовых сборок

| RID | Артефакт | Запуск |
| ----- | ---------- | -------- |
| `osx-arm64` | `publish/osx-arm64/ARIA.app` | `publish/osx-arm64/ARIA.app/Contents/MacOS/ARIA` |
| `osx-x64` | `publish/osx-x64/ARIA.app` | `publish/osx-x64/ARIA.app/Contents/MacOS/ARIA` |
| `linux-x64` | `publish/linux-x64/aria-linux-x64.tar.gz` | распаковать, `./ARIA/aria` |
| `win-x64` | `publish/win-x64/aria-win-x64.zip` | распаковать, `ARIA\ARIA.App.exe` |

Артефакты `publish/` в git не коммитятся (gitignore). Проверенные стенды: `win-x64`, `osx-arm64`, `linux-x64` (docs/adr/0004-platform-matrix.md). Остальные RID собираются, но «проверено» не заявляется.

## Использование

```bash
ARIA --selftest                      # дым-тест: шина, playback smoke, remote, автосейв
ARIA --remote-id NAME                # показать/сменить идентификатор pairing
ARIA --remote-set-password SECRET    # задать пароль пульта
ARIA --remote-reset-password         # сбросить пароль на случайный
```

Пульт: открыть в браузере URL из панели Remote (`http://<host>:<port>/?id=...&key=...`) или сканировать QR. Порт динамический (loopback, fallback на свободный при конфликте).

Горячие клавиши по умолчанию (настраиваются в Settings, хранятся в `hotkeys.json`):

| Жест | Действие |
| ------ | ---------- |
| `Space` | play |
| `Escape` | pause |
| `Ctrl+Shift+P` | panic |
| `Ctrl+N` | next |
| `Ctrl+R` | replay |
| `Ctrl+L` | lock |
| `Ctrl+T` | toggle-script |
| `Ctrl+Shift+C` | reset-clock |

## Конфигурация и данные

Каталог данных — `SpecialFolder.ApplicationData/ARIA`:

| ОС | Путь |
| ---- | ------ |
| Windows | `%APPDATA%\ARIA` |
| macOS | `~/.config/ARIA` |
| Linux | `~/.config/ARIA` |

| Файл | Содержимое |
| ------ | ------------ |
| `show.json` | снапшот Show: плейлисты, очередь, позиции, настройки, сценарии |
| `library.db` | библиотека Track (SQLite) |
| `waveforms.db` | кэш waveform (SQLite) |
| `remote-auth.json` | pairing идентификатор + пароль пульта |
| `hotkeys.json` | переопределения горячих клавиш |

Бэкап для переноса шоу на другую машину: скопировать `show.json` + `library.db` + аудиофайлы (пути должны совпасть). Пароль пульта после переноса сбросить (`--remote-reset-password`).

## Разработка

Структура: `src/` — `ARIA.App` (Avalonia UI), `ARIA.Core` (домен, команды, ShowController), `ARIA.Audio` (движок, miniaudio-шим), `ARIA.Persistence` (SQLite + снапшоты), `ARIA.Remote` (HTTP/WS-хост пульта). Тесты зеркально в `tests/`. Терминология домена — CONTEXT.md, решения — docs/adr/.

```bash
dotnet build        # сборка всего солюшена, 0 предупреждений (TreatWarningsAsErrors)
dotnet test         # весь солюшен; при флаке Remote/App.Tests — перезапуск (см. ниже)
dotnet test tests/ARIA.Core.Tests
```

Правила: file-scoped namespaces, records, иммутабельность; ноль комментариев в коде (контракты — именами и тестами); тесты только через интерфейсы (`ICommandBus`, `IAudioEngine`, `ILibraryStore`/`ISnapshotStore`).

## Тесты

```bash
dotnet test         # полный прогон перед каждым коммитом
```

Известные флаки среды (не продукта): Remote-тесты под параллельной нагрузкой теряют loopback-кадры; Avalonia.Headless стартует через раз (`DefaultRenderLoop.Add` cross-thread). Лечится перезапуском. Детали — AGENTS.md.

## Сборка под архитектуры

```bash
sh native/aria-shim/build.sh <RID>   # только нативный шим
sh scripts/publish.sh <RID>          # шим + dotnet publish + упаковка
```

Матрица (хост сборки — macOS arm64):

| RID | Нативный шим | Тулчейн | Упаковка | Проверка на хосте |
| ----- | -------------- | --------- | ---------- | ------------------- |
| `osx-arm64` | `libaria_shim.dylib` (arm64) | `cc -arch arm64`, Apple frameworks | `ARIA.app` bundle, ad-hoc codesign | `--selftest` напрямую |
| `osx-x64` | `libaria_shim.dylib` (x86_64) | `cc -arch x86_64`, Apple frameworks | `ARIA.app` bundle, ad-hoc codesign | `--selftest` через `arch -x86_64`, либо Rosetta |
| `linux-x64` | `libaria_shim.so` (stripped) | `zig cc -target x86_64-linux-gnu`, `-lpthread -ldl -lm` | `ARIA/` + `aria-linux-x64.tar.gz` | структурно (файлы, ELF-тип); рантайм — на стенде linux-x64 |
| `win-x64` | `aria_shim.dll` | `x86_64-w64-mingw32-gcc`, `-lole32 -lwinmm -luuid -lversion -ladvapi32` | `ARIA/` + `aria-win-x64.zip` | структурно (файлы, PE-тип); рантайм — на стенде win-x64 |

Исходники шима: `native/aria-shim/` (`aria_shim.c` — miniaudio-движок + ring buffer, `stb_vorbis.c` — декодер OGG, `miniaudio.h`). Собранные бинарники лежат в `runtimes/<RID>/native/` и коммитятся (как и `native/aria-shim/libaria_shim.dylib` для хоста); `publish/` — нет. Резолв в рантайме — `AriaShim.Resolve`: `runtimes/<RID>/native/<имя>` рядом со сборкой, затем рядом с процессом, затем системный поиск. `backend=1` (`ma_backend_null`) — беззвучный null-бэкенд для selftest/тестов.

## Поддержка

- Шоу не стартует / трек помечен Faulted: пометка сессионная, снимается автопроверкой при следующем обращении; проверить путь к файлу и права чтения.
- Нет звука, UI жив: проверить, что рядом с бинарником лежит `runtimes/<RID>/native/` с шимом; на Linux — ALSA/PulseAudio устройство по умолчанию; на Windows — устройство вывода по умолчанию.
- Пульт не открывается: хост и телефон в одном LAN; порт смотреть в панели Remote (динамический); reverse-proxy не поддерживается.
- Потеря pairing: `--remote-reset-password`, отсканировать новый QR.
- Сброс настроек клавиш: удалить `hotkeys.json`, перезапустить (вернутся дефолты).
- Чистый сброс состояния: остановить приложение, удалить `show.json` (шоу), при необходимости `library.db`/`waveforms.db` (пересканируются при импорте).

## Contributing

1. Форк, ветка `feature/<имя>`.
2. Батч = зелёные `dotnet build` + `dotnet test`, 0 предупреждений.
3. Коммит: заголовок ≤72 символов, тело — что/зачем/решения/число тестов; один логический батч = один коммит; push после каждого батча. Никаких force-push и правок чужих коммитов.
4. PR в `main`.

## Changelog

Релизов с CHANGELOG.md пока нет. История — `git log` (`origin/main`, ветки `batch*`).

## Ссылки

- Репозиторий: <https://github.com/vladislavsaltanov/ARIA>
- Issues: <https://github.com/vladislavsaltanov/ARIA/issues>
- ADR: docs/adr/ (платформенная матрица — 0004-platform-matrix.md)
- Язык домена: CONTEXT.md, правила агентов: AGENTS.md

## License

MIT — см. [LICENSE](LICENSE).

## Verify

```bash
test -f README.md && [ "$(grep -c '^## ' README.md)" -ge 7 ] && echo README-OK
```
