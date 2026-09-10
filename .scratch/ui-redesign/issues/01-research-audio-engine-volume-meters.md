# Аудит аудио-движка: громкость, метринг, LUFS

Type: research
Status: resolved

## Question

Что аудио-движок уже умеет и что нужно добавить, чтобы в UI появились: ползунок громкости и LUFS-метр мастер-выхода?

Факты собрать с точностью до файла:строки:
- Есть ли в `ARIA.Core/Commands` и `ARIA.Audio` команда/механизм мастер-громкости (или только per-track gain)? Как регулировалась бы громкость через `ICommandBus`?
- Есть ли метринг (пики/RMS) в микшере? Где технически можно снять тап для измерителя?
- Что нужно для momentary LUFS (K-weighting: pre-filter + RLB high-pass, 400ms скользящее окно)? Возможно ли это в колбэке с нулевыми аллокациями (инвариант проекта), или считать на отдельном потоке из буфера?
- Есть ли измерение на Preview-канале и нужно ли оно в UI?

Ответ: таблица «есть / нет / где», вердикт о феасибилити LUFS-метра и список того, чего не хватает движку.

## Answer

- Мастер-громкость уже есть end-to-end: команда `SetMasterGain` (Commands.cs:51) → `ShowController.OnSetMasterGain` (ShowController.cs:721-731, диапазон −80…+12 дБ) → `IAudioEngine.SetMasterGain` (EnginePort.cs:61) → `ApplyMasterGain` в рендер-потоке (AriaAudioEngine.cs:74-75, 126-137). Персистится в снапшоте, ходит через Remote (`set_master_gain`, CommandCodec.cs:69). Новых команд не нужно; UI пока `MixerState` не читает.
- Метринг: пик считается в `MixerBus.Peak` (MixerBus.cs:80, 404-416), но в проде не читается; `MeterNode` (Peak/RMS, DspNodes.cs:89-121) готов и проверен на ноль аллокаций, но в граф не вставлен. LUFS нет нигде.
- Тап мастер-измерителя — `AriaAudioEngine.RenderLoop` между `ApplyMasterGain()` и `_sink.Write` (AriaAudioEngine.cs:108-109), после гейна.
- Verdict LUFS: феасибильно прямо в C#-рендер-пути (нативный колбэк устройства в C# не заходит). K-weighting = 2 биквад-фильтра на канал + окно 400 мс в преаллоцированном ring — ноль аллокаций, <0.1% CPU; отдельный поток не нужен, publish по образцу `PlaybackMonitor` (latest-value).
- Preview: `StreamBus.Preview` — мёртвый enum; движок игнорирует `options.Bus` (AriaAudioEngine.cs:43-56), контроллер шлёт только Main (ShowController.cs:878). Метринг превью не нужен, пока не реализовано само воспроизведение превью.
- Шины: одна плоская `MixerBus` (сумма голосов fader+gain, MixerBus.cs:281-331) → мастер-гейн C# → нативный ring → `data_callback` устройства (aria_shim.c:22-68); сабгрупп нет. Нативный `aria_engine_set_master_gain` — второй, отключённый механизм гейна.

Полный отчёт: [research/01-audio-engine.md](../research/01-audio-engine.md)
