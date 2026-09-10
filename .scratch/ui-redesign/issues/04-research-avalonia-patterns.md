# Avalonia-паттерны: waveform, drawer, иконки, производительность Render

Type: research
Status: resolved

## Question

ARIA на Avalonia 12.1.2. Какие паттерны использовать для нового UI, чтобы не упереться в платформу:

- Кастомный рендер waveform (играющий трек, курсор, cue-точки): `Control.Render` + `DrawingContext`, `WriteableBitmap`, готовые библиотеки? Инвариант: ноль аллокаций в Render — как рисуют волну без аллокаций (кэшированная геометрия, потоковые сегменты)?
- Выезжающая боковая панель (drawer) поверх/рядом с контентом: `SplitView` (DisplayMode), `Expander`, анимации — что идиоматично в Avalonia 12 и дёшево по ресурсам.
- Иконки вместо текстовых кнопок (Spotify/AIMP-стиль): какой набор глифов/шрифтов иконок использовать без внешних зависимостей (PathIcon + StreamGeometry?), лицензии.
- Компактный транспорт-бар: фиксированная высота, слайдер громкости, тонкие индикаторы — примеры рабочих паттернов (флажки Slider, шаблоны).
- Темплаты клавиш/тултипов для обнаруживаемости хоткеев (тултип с жестом, help-оверлей по `?`).

Ответ: рекомендованный паттерн на каждый пункт с обоснованием и ссылкой на доки/примеры.

## Answer

- Waveform: custom-drawn Control — `Render(DrawingContext)` с кэшированными `StreamGeometry`/`Pen`/кистями, пересборка геометрии только при смене данных/размера; позиция курсора — styled property с `AffectsRender`; курсор/cue — `DrawLine` кэшированными Pen. WriteableBitmap/ICustomDrawOperation избыточны при 25 pts/s. Шаблон подписки — `TransportViewModel` (Post в UI-поток). Риски: аллокации Push* не измерены; stroke→fill у иконок не относится, но UI-Render не покрыт тестом на аллокации — добавить по образцу MixerBusTests.
- Drawer: SplitView `PanePlacement="Right"` + `DisplayMode="CompactOverlay"` (или Overlay), `IsPaneOpen` в VM; анимация (open 0.2 с / close 0.1 с), light-dismiss встроены в тему Fluent.
- Иконки: PathIcon + StreamGeometry-ресурсы; глифы Lucide (ISC+MIT) или Feather (MIT) — вшить path-строки, лицензии сохраняются. Главный риск: их SVG stroke-based, PathIcon рисует заливкой — нужна конвертация stroke→outline или fill-набор (проверить прототипом).
- Транспорт: Slider с кастомным ControlTheme (тонкий трек), ToggleButton play/pause (в 12 события Checked/Unchecked удалены → IsCheckedChanged), ToolTip.Tip с текстом+жестом.
- Хоткеи: жест в ToolTip руками (готового механизма нет), help-оверлей — KeyBinding F1/OemQuestion + оверлей; «?» в KeyGesture не парсится. Данные жестов уже есть в HotkeyConfig.
- Полный отчёт с фактами и ссылками: [research/04-avalonia-patterns.md](../research/04-avalonia-patterns.md)