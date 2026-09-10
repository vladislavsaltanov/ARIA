# Research 01: аудио-движок — мастер-громкость, метринг, LUFS

Type: research · Status: resolved · Дата: 2026-09-10
Вопрос: [.scratch/ui-redesign/issues/01-research-audio-engine-volume-meters.md](../issues/01-research-audio-engine-volume-meters.md)

Метод: чтение src/ARIA.Audio, src/ARIA.Core, src/ARIA.Remote, native/aria-shim/aria_shim.c, tests/ARIA.Audio.Tests и tests/ARIA.Core.Tests. Код не писался, build/test не запускались.

## Ответы (есть / нет / где)

| # | Вопрос | Вердикт | Где (файл:строка) |
|---|--------|---------|-------------------|
| 1a | Команда мастер-громкости в Core | **есть** | `SetMasterGain(GainDb)` — src/ARIA.Core/Commands/Commands.cs:51; диапазон −80…+12 дБ — src/ARIA.Core/Runtime/ShowController.cs:11-12 |
| 1b | Проводка команды через ICommandBus | **есть, end-to-end** | диспетчер — src/ARIA.Core/Runtime/ShowController.cs:131-133; обработчик `OnSetMasterGain` (валидация, `_engine.SetMasterGain`, `EmitMixer`) — ShowController.cs:721-731; шов `IAudioEngine.SetMasterGain` — src/ARIA.Core/Playback/EnginePort.cs:61 |
| 1c | Применение громкости в движке | **есть** | `AriaAudioEngine.SetMasterGain` (дБ→линейно, `Volatile.Write`) — src/ARIA.Audio/AriaAudioEngine.cs:74-75; применение к каждому блоку в рендер-потоке `ApplyMasterGain` — AriaAudioEngine.cs:107-108, 126-137 |
| 1d | Персистентность и Remote | **есть** | `RestoreShow.MasterGainDb` — Commands.cs:60-66 (применение — ShowController.cs:245-247); снапшот — src/ARIA.Persistence/SnapshotStore.cs:13,75,87,100; автосейв — src/ARIA.Persistence/ShowAutosaver.cs:72; Remote-кодек `set_master_gain` — src/ARIA.Remote/CommandCodec.cs:69 |
| 1e | Второй (нативный) мастер-гейн — не подключён | **есть, мёртвый код** | `MiniaudioSink.SetMasterGain` — src/ARIA.Audio/MiniaudioSink.cs:64 → `aria_engine_set_master_gain` — src/ARIA.Audio/Native/AriaShim.cs:29,98; применяется в колбэке устройства — native/aria-shim/aria_shim.c:19,63-67,232-239. `AriaAudioEngine` его не вызывает (остаётся 1.0); юзается только тестом tests/ARIA.Audio.Tests/MiniaudioSinkTests.cs:33-40 |
| 1f | Громкость в UI сегодня | **нет** | `TransportViewModel` и вьюхи не читают `MixerState`/`MixerDelta` (grep по ARIA.App — 0 попаданий); состояние есть в `Snapshot()` — ShowController.cs:143-151 |
| 2a | Пик-метр микшера | **частично** | `MixerBus.Peak` — src/ARIA.Audio/MixerBus.cs:80; вычисляется за `Render` по всему выходу `ComputePeak` — MixerBus.cs:149, 404-416. Пик моно-суммарный, без/channel, без hold-логики наружу. В проде никем не читается (только тесты tests/ARIA.Audio.Tests/MixerBusTests.cs:18,31,36,63,78,85,97,125,149,174) |
| 2b | RMS/метр-нода | **есть класс, не включён в граф** | `MeterNode` (Peak/Rms/RunningPeak) — src/ARIA.Audio/DspNodes.cs:89-121; тест — tests/ARIA.Audio.Tests/DspNodeTests.cs:50-62; входит в тест нулевых аллокаций — DspNodeTests.cs:77-98. В `MixerBus` не вставлен ни на голос, ни на мастер (orphan) |
| 2c | Тап для мастер-измерителя | **точка есть** | Правильная точка (после мастер-гейна, ровно то, что уходит в устройство): `AriaAudioEngine.RenderLoop`, между `ApplyMasterGain()` и `_sink.Write(_block)` — AriaAudioEngine.cs:108-109. Альтернатива до гейна — хвост `MixerBus.Render` рядом с `ComputePeak` (MixerBus.cs:149). Поток публикации для UI уже имеет образец: `PlaybackMonitor` (latest-value) — src/ARIA.Core/Playback/PlaybackMonitor.cs:54-74, троттлинг 10 Гц — AriaAudioEngine.cs:139-161 |
| 3 | LUFS / K-weighting | **нет** | В C#-коде ничего; единственные «LUFS» в репо — WAV bext-метаданные miniaudio — native/aria-shim/miniaudio.h:62165-62166 (не метринг) |
| 4 | Preview-канал и его метринг | **нет** | `StreamBus.Preview` существует только как enum — src/ARIA.Core/Playback/EnginePort.cs:8-12; `AriaAudioEngine.StartStream` игнорирует `options.Bus` (берёт только Markers) — AriaAudioEngine.cs:43-56; контроллер всегда шлёт `StreamBus.Main` — ShowController.cs:878. Ни канала прослушки, ни метринга |
| 5 | Шины / мастер-сумма | **одна плоская шина** | Классы: `MixerBus` (голоса `_voices`, командная очередь) — MixerBus.cs:17, 59-64; на голос: `FaderNode`+`GainNode` — src/ARIA.Audio/DspNodes.cs:10-87; суммирование в `RenderChunk` — MixerBus.cs:281-331. Финальный микс до вывода: `RenderLoop` — AriaAudioEngine.cs:102-116 (`_mixer.Render(_block)` → `ApplyMasterGain` → `_sink.Write`). Вывод в устройство: `MiniaudioSink.Write` → пиннед-скратч → `aria_engine_write` (ring) — MiniaudioSink.cs:43-62, native/aria-shim/aria_shim.c:181-214; колбэк устройства `data_callback` тянет из нативного ring и применяет native `master_gain` — aria_shim.c:22-68. Сабгрупп/шин/роутинга нет |

Тесты инвариантов: ноль аллокаций на `MixerBus.Render` — tests/ARIA.Audio.Tests/MixerBusTests.cs:178-197; пайплайн DSP (fader+gain+meter) — DspNodeTests.cs:77-98. Мастер-гейн end-to-end — tests/ARIA.Audio.Tests/AriaAudioEngineTests.cs:80-95; валидация диапазона — tests/ARIA.Core.Tests/PlaybackTests.cs:250-260; restore — tests/ARIA.Core.Tests/RestoreShowTests.cs:43-71.

## Вердикт по феасибилити momentary LUFS

**Феасибильно прямо в C#-рендер-пути, отдельный поток не нужен.**

Ключевой архитектурный факт: настоящий аудио-колбэк устройства — только нативный `data_callback` (aria_shim.c:22-68), он не заходит в C#. «Колбэком» в терминах инварианта проекта является выделенный рендер-поток `aria-render` (AriaAudioEngine.cs:37), гоняющий блоки 512×float×2 при 48 кГц (~21 блок/с); тесты `GC.GetAllocatedBytesForCurrentThread` защищают именно `MixerBus.Render` и DSP-пайплайн.

Оценка стоимости K-weighting на блок 512 фреймов:
- **Фильтры**: BS.1770 stage 1 (high-shelf) + stage 2 (RLB high-pass) — два биквад-IIR на канал; состояние = 2 двойки на фильтр на канал, коэффициенты при 48 кГц — табличные константы. Поля объекта, ноль аллокаций.
- **Окно 400 мс**: 19200 фреймов = 37.5 блоков по 512 → скользящее окно энергий блоков в преаллоцированном ring-буфере double'ов (~38 слотов) с катящимся sum. Возможна точная граница с дробным последним блоком либо классическое допущение выравнивания по блокам (расхождение <11 мс, на дисплей не влияет).
- **Числитель**: `meanSquare` → LUFS = −0.691 + 10·log10(meanSquare) — один `Math.Log10` на блок; абсолютный гейт −70 LUFS — просто пол отображения. Никаких аллокаций: `Math` не аллоцирует.
- **CPU**: ~2048 сэмплов × 2 канала × 2 фильтра × ~5 операций ≈ микросекунды на блок — <0.1% ядра.
- **Публикация**: latest-value (`Volatile.Write` float+timestamp) по образцу `PlaybackMonitor`; UI опрашивает таймером. Аллокаций нет, локов нет.

Итог: инвариант «ноль аллокаций в колбэке/Render» соблюдается тривиально; отдельный поток из буфера не нужен и добавил бы сложность без выгоды. Важно: тап должен стоять **после** `ApplyMasterGain` (AriaAudioEngine.cs:108-109), иначе метр не отражает реальный выход при отличном от 0 дБ мастер-гейне. Поведение при тишине/паузе/после PANIC: энергия → 0, LUFS → −∞; нужен display floor (−70 LUFS по абсолютному гейту BS.1770).

## Чего не хватает движку (только перечень)

1. **Сим метринга → UI**: API публикации метрик из движка (latest-value монитор/событие по образцу `PlaybackMonitor` — PlaybackMonitor.cs, троттлинг — AriaAudioEngine.cs:139-161). Сегодня `MixerBus.Peak` никем не читается, наружу не торчит.
2. **Тап мастер-метра в `AriaAudioEngine.RenderLoop`** после `ApplyMasterGain()` (AriaAudioEngine.cs:108-109) — точка измерения есть, измерения нет.
3. **K-weighting DSP**: класс(ы) двух биквад-фильтров (high-shelf pre-filter + RLB high-pass, коэффициенты BS.1770 @ 48 кГц) и скользящего окна 400 мс — в `DspNodes.cs` их нет.
4. **LUFS-конвертация и дисплейная логика**: −0.691 + 10·log10, абсолютный гейт −70 LUFS, поведение при тишине.
5. **Подключение `MeterNode` в граф** (если нужны Peak/RMS рядом с LUFS): нода готова (DspNodes.cs:89-121), но не вставлена ни в голоса (`RenderChunk` после fader+gain — MixerBus.cs:297-298), ни в мастер.
6. **Опционально — per-Deck метринг**: тап в `MixerBus.RenderChunk` по голосу; нужен, если UI захочет измеритель не только мастера.
7. **Решение по второму мастер-гейну**: нативный `aria_engine_set_master_gain` (aria_shim.c:232-239) применяется в колбэке устройства, но движком не используется — оставить единственным владельцем гейна C#-сторону (ApplyMasterGain) либо явно разделить роли.
8. **Команды ICommandBus новые не нужны**: `SetMasterGain` существует end-to-end (включая Remote-кодек и снапшот); LUFS-метр — телеметрия чтения, а не команда.
9. **Preview целиком** (если решит спека): роутинг `StreamBus` в `StartStream` (сейчас игнорируется — AriaAudioEngine.cs:43-56), отдельная шина/выход в `MixerBus`, движковый API старта превью из `ShowController` — метринг превью станет побочным продуктом того же тапа; для текущего UI-спека необходимости нет (Preview не воспроизводится вообще).

## Замечания для спеки

- Ползунок громкости UI подключается к готовому `MixerState.MasterGainDb` (StateEvents.cs:48; снапшот — ShowController.cs:151) и команде `SetMasterGain`; UI-ограничения диапазона: −80…+12 дБ.
- LUFS-метр мастера реален без новых потоков и без правок натива; единственная правка внутри движка — точка съёма в `RenderLoop` + классы фильтров/окна в ARIA.Audio.
- Обновление метра: блоки идут ~21 Гц (512/48к); `PublishPosition` троттлит до ~100 Гц максимум — для метра достаточноpublish'а на каждый блок или троттлинга 20–30 Гц.
