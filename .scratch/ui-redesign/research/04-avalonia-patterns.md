# Ресерч 04: Avalonia-паттерны — waveform, drawer, иконки, транспорт, обнаруживаемость

Type: research · Status: resolved · Дата: 2026-09-10
Вопрос: [.scratch/ui-redesign/issues/04-research-avalonia-patterns.md](../issues/04-research-avalonia-patterns.md)

Метод: чтение доков docs.avaloniaui.net (страницы указаны в каждом разделе), исходников Avalonia на GitHub (тема Fluent: `SplitView.xaml`), GitHub API (лицензии иконок, поиск примеров), локального кода src/ARIA.App и tests/. Код не писался, build/test не запускались.

Внешне проверено: страницы доков (SplitView, PathIcon, ToolTip, Slider, Custom rendering, Custom-drawn controls, Control transitions, Keyboard and hotkeys, Mouse and keyboard shortcuts, Avalonia 12 breaking changes), raw-файл `src/Avalonia.Themes.Fluent/Controls/SplitView.xaml` (master), LICENSE Feather (MIT) и Lucide (ISC+MIT). Не проверено внешне (явно помечено ниже): поведение аллокаций `DrawingContext.Push*` в 12.x, WriteableBitmap-доки (страница не найдена, использован API-знание), внутренности waveform-рендера найденных community-плееров.

---

## 1. Waveform-рендер: Render(DrawingContext) с кэшированной геометрией

**Рекомендация: кастомный custom-drawn Control — наследник `Control`, override `Render(DrawingContext)`, кэшированный `StreamGeometry` + `Pen` + `IBrush`, пересборка геометрии только при смене данных/размера.**

Обоснование:

- Доки «Custom rendering» прямо фиксируют требования и паттерн: «Render is called on the UI thread. Keep drawing operations fast and avoid allocations where possible», «Reuse Pen, Brush, and FormattedText objects... Store them as fields and recreate only when their inputs change» (docs.avaloniaui.net/docs/graphics-animation/custom-rendering, раздел Performance considerations).
- Данные дёшевы: `WaveformPeaks(TrackId, PointsPerSecond, SampleRate, ImmutableArray<PeakPoint>)`, `PeakPoint(Min, Max)` (src/ARIA.Core/Model/WaveformPeaks.cs:7-10), 25 точек/сек (TrackImporter.cs:21, по research 02 §5). Для бара шириной W px один бар = несколько пиков: проход по ImmutableArray в преаллоцированную геометрию.
- Схема «без аллокаций в Render»: поля контрола — `StreamGeometry` (строится `Open()`/`StreamGeometryContext` один раз на смену данных или resize), `Pen` (одна-две шт.), immutable-кисти (`SolidColorBrush`, создан в конструкторе). Render — только `DrawGeometry(brush, pen, geometry)`, `DrawLine(pen, p1, p2)` для курсора/cue (кэшированные Pen), `DrawRectangle`. Пересборка StreamGeometryContext аллоцирует, но это вне Render — при смене трека и при resize (не per-frame).
- Инвалидация: `AffectsRender<T>(PeaksProperty)` для данных; позицию курсора лучше вводить как styled property (double 0..1) с `AffectsRender` — доки показывают ровно этот паттерн на примере ProgressRing (custom-rendering: «AffectsRender registers a callback so that any change... triggers InvalidateVisual() automatically»), либо `InvalidateVisual()` вручную из подписки на PlaybackMonitor (custom-drawn controls: «Use manual invalidation sparingly»).
- Сравнение альтернатив:
  - **WriteableBitmap** — per-pixel растеризация на CPU (Lock/Write/CopyPixels) на каждый кадр обновления; оправдана для плотных визуализаций (спектрограмма, thousands of bars per frame). Для 25 pts/s и векторной полосы избыточна и дороже. ⚠️ Внешне не проверено: страница WriteableBitmap в доках не найдена по известным URL (404), сверить API по `/api/avalonia/media/imaging/writeablebitmap` при прототипе.
  - **FormattedText** — для числовых меток времени поверх волны; сам по себе аллоцирует на конструкторе — только кэшированный (доки прямо советуют reuse). Для полосы волны не инструмент.
  - **ICustomDrawOperation (SkiaSharp lease)** — обход кэша сцены, SkiaPaint-объекты на каждый кадр аллоцируют; docs: «bypasses Avalonia's scene graph caching. Use it only when you need SkiaSharp-level control». Не нужен при 25 pts/s.
- Примеры на GitHub: найдены живые Avalonia-плееры (Noctis, MIT, 214★, активен 2026; osuplayer, MIT, 102★; Resona, MIT, Avalonia 11 — github.com/heartached/Noctis, github.com/Founntain/osuplayer, github.com/mikegoatly/Resona; поиск GitHub API «avalonia music player»). ⚠️ Их waveform-интернал не проверен (код не читал). Официальный образец кастомного рендера — Avalonia.Samples `SnowflakesControlSample` (ссылка из доков custom-drawn controls).
- Риск: фактические аллокации внутри `DrawingContext.PushClip/PushOpacity` (возвращают IDisposable) в 12.x не проверены — при per-frame обновлении курсора избегать Push-ов либо измерить. Тест-харнесс в проекте уже есть: `GC.GetAllocatedBytesForCurrentThread` (MixerBusTests.cs:177-197, DspNodeTests.cs:76-98) — рекомендую аналогичный тест для waveform-контрола. Важно по AGENTS.md:39: инвариант «ноль аллокаций в аудио-callback/Render» формально относится к аудио-рендер-потоку, UI-слой тестами не покрыт (research 02 §6) — расширение инварианта на UI-Render это осознанное решение, не существующее требование.
- Интеграция (факты): позиция приходит как latest-value `PositionSnapshot(Deck, FilePosition, Remaining)` из `PlaybackMonitor.Changed` (~10 Гц из render-потока, PlaybackMonitor.cs:54-74; троттлинг — AriaAudioEngine.cs:139-161 по research 01 §2c). Шаблон подписки с маршалингом — `TransportViewModel.MonitorSubscription` + `SynchronizationContext.Post` (TransportViewModel.cs:49-52, 104-114, 136-158). `IWaveformStore.Load` в проде пока никем не вызывается — waveform-контрол будет первым потребителем (research 02 §5).

## 2. Выезжающая панель: SplitView, PanePlacement="Right"

**Рекомендация: `SplitView`, `PanePlacement="Right"`, `DisplayMode="CompactOverlay"` (или `Overlay`, если компактная полоса не нужна), `IsPaneOpen` в VM.**

Обоснование:

- Идиоматичность: доки прямо описывают этот паттерн — «Navigation sidebar pattern» с MVVM: `IsPaneOpen="{Binding IsPaneOpen}"`, `[ObservableProperty]`, `TogglePaneCommand` (docs.avaloniaui.net/controls/layout/containers/splitview).
- Анимация встроена: тема Fluent (raw src/Avalonia.Themes.Fluent/Controls/SplitView.xaml, master) содержит `DoubleTransition` на Width/Height `PART_PaneRoot` для всех режимов и сторон: open 0.2 с, close 0.1 с, easing `0.1,0.9,0.2,1.0`, ресурсы `SplitViewPaneAnimationOpen/CloseDuration`. Ничего анимировать руками не нужно.
- Overlay/CompactOverlay: панель поверх контента (ZIndex 100), открытая overlay-панель показывает LightDismissLayer — клик мимо закрывает (тема: `^:overlay:open /template/ Rectangle#LightDismissLayer → IsVisible=True`). CompactOverlay оставляет полосу `CompactPaneLength` (по умолчанию 48 px) для иконки-кнопки — совпадает с постоянным «drawer» из спеки.
- Grid-анимация вручную возможна (Transitions на Width — docs/graphics-animation/control-transitions), но это ручная реплика SplitView без light-dismiss и семантики pane; Expander — вертикальный аккордеон, не боковой drawer.
- Popup/Flyout — для транзиентных меню/подсказок, не для постоянной панели: отдельное окно-попап, нет места в layout, поведение фокуса сложнее. Для «панель всегда в одном месте» — не вариант.
- Поведение фокуса/клавиатуры при Overlay-открытии внешне не проверено (доков нет) — ⚠️ проверить в прототипе: не зависает ли фокус в закрытой панели, поведение Tab.
- Риск: overlay-режим при открытии перекрывает контент — под волной/счётчиком нужно следить, чтобы панель не накрывала транспорт (ZIndex панели 100 в теме). Размеры по умолчанию: OpenPaneLength 320, CompactPaneLength 48 (ресурсы темы).

## 3. Иконки: PathIcon + StreamGeometry-ресурсы, глифы Lucide/Feather

**Рекомендация: `PathIcon` с `Data="{StaticResource ...}"` на `StreamGeometry`-ресурсы, вшитые в Application/Window.Resources; глифы — Lucide (или Feather).**

Обоснование:

- Доки PathIcon (docs.avaloniaui.net/controls/media/pathicon): рендерит vector-геометрию, масштабируется, перекрашивается через наследуемый `Foreground` — ровно то, что нужно монохромному Nothing-минимализму (ADR-0006). Официальный паттерн ресурсов: `<Application.Resources><StreamGeometry x:Key="...">M...</StreamGeometry></Application.Resources>` + `{StaticResource}`. Готовая галерея geometry-строк от Avalonia: avaloniaui.github.io/icons.html.
- Лицензии (проверено по LICENSE-файлам):
  - **Feather** (github.com/feathericons/feather, 288 иконок) — MIT (GitHub API: SPDX MIT; © Cole Bemis 2013-present).
  - **Lucide** (github.com/lucide-icons/lucide, 1500+, fork Feather, 24k★, активен 2026) — ISC (LICENSE © Lucide Icons and Contributors), при этом часть иконок, унаследованных от Feather (перечень в LICENSE: play, pause, square, music, x, chevron-*, help-circle, headphones и др. — транспорт-набор там есть), дополнительно MIT.
  - Обе лицензии разрешают вшивание path-строк в ресурсы приложения; условие — сохранить copyright/лицензионное уведомление (включить LICENSE в репо рядом с иконками).
- Вшивание: скопировать SVG path `d="..."` в `<StreamGeometry x:Key="icon_play">` — синтаксис SVG path совпадает со StreamGeometry. Без NuGet-зависимостей и шрифтовых иконок.
- Риск (⚠️ не проверено внешне/доками): SVG у Lucide/Feather — **stroke-based** (stroke-width 2, fill none), а `PathIcon` рисует `Data` заливкой `Foreground`. Строки path из их SVG — это осевые линии, при заливке иконка схлопнется. Нужен один из путей: конвертация stroke→outline при генерации ресурсов (одноразовый скрипт/тулинг), набор с fill-вариантами (Fluent icons галерея Avalonia — filled/regular), или проверка, что Fluent-набор закрывает потребность. Это главный риск пункта — закрыть прототипом (тики 11/12).

## 4. Компактный транспорт-бар: Slider с ControlTheme, ToggleButton, ToolTip

**Рекомендация: стандартный `Slider` с собственным ControlTheme (тонкий трек, ручка-точка), `ToggleButton` с двумя PathIcon-состояниями, `ToolTip.Tip` с текстом+жестом; фиксированная высота бара через Grid+Height, масштабирование — MinHeight/MinWidth touch-целей (ADR-0006).**

Обоснование и факты:

- Slider (docs/controls/input/selectors/slider): `Value/Minimum/Maximum`, `SmallChange/LargeChange` (клавиши), `TickPlacement/TickFrequency/IsSnapToTickEnabled`, `Orientation`, `IsDirectionReversed`. MVVM-биндинг Value — официальный пример с CommunityToolkit `[ObservableProperty]` (доки показывают ровно это). Идиома «тонкого» слайдера — перекрытие `ControlTheme`/шаблона Slider (доки не дают готового шаблона тонкого слайдера; шаблон Fluent у Slider стандартно толстый — ⚠️ переписывать шаблон придётся руками, опираясь на `Track`/thumb-части; готового публичного примера не найдено — прототип).
- ToggleButton play/pause: `IsChecked` → VM-статус; иконка переключается по псевдоклассу `:checked` в стилях (PathIcon с разными Data). ⚠️ Avalonia 12 breaking changes: события `ToggleButton.Checked/Unchecked` удалены → использовать `IsCheckedChanged` (docs/avalonia12-breaking-changes, таблица Removed members). Плюс: VM уже знает статус (`TransportStatus` → `StatusText`, TransportViewModel.cs:118-124) — иконку можно вести и от VM.
- ToolTip: `ToolTip.Tip` (строка или произвольное содержимое), `Placement`, `ShowDelay` (400 мс по умолчанию), `ShowOnDisabled` (нужно для PANIC в disabled-состоянии), `ToolTipOpening/ToolTipClosing` (docs/controls/feedback/tooltip). Содержимое тултипа — любые контролы: можно TextBlock + мини-лейбл жеста (см. §5).
- Фиксированная высота бара: банальный Grid с `RowDefinitions="Auto,Auto,*"` (контент) + нижний бар с заданной Height; в Avalonia нетособой идиомы «бара» — это обычный Grid/Border; масштабирование — MinHeight/MinWidth на интерактивных элементах (touch-цели ≥44 px по духу ADR-0006; конкретное значение — решение спеки, не платформенное).

## 5. Обнаруживаемость хоткеев: KeyGesture в ToolTip вручную, help-оверлей по Overlay/KeyBinding

**Рекомендация: текст жеста в `ToolTip.Tip` рядом с каждой командой (статически в XAML или из HotkeyConfig в code-behind); help-оверлей — собственный Grid/Panel поверх контента по IsVisible + KeyBinding на «?»/F1; управление фокусом — KeyboardNavigation.**

Обоснование:

- Платформа: `KeyBinding` (Window.KeyBindings / KeyBindings контрола, `Gesture="Ctrl+S"`), `HotKey` на ICommandSource (MenuItem/Button), разбор жеста как KeyGesture; на macOS `Ctrl` автоматически мапится в Cmd (docs/input-interaction/keyboard-and-hotkeys; mouse-and-keyboard-shortcuts: «Use Cmd instead of Ctrl... bind both variants»). ⚠️ Готового механизма «ToolTip из KeyGesture» в Avalonia нет — связка делается руками (ToolTip.Tip = имя + жест). Спец-символ «?» в gesture-строке не парсится как Key (Enum.Parse по Key enum) — для триггера оверлея использовать `KeyBinding Gesture="F1"`/`OemQuestion` или существующий HotkeyService (он строит жесты из KeyDown — MainWindow.axaml.cs:40, 55-93) — ⚠️ проверить точный ключ прототипом.
- Готового «help-оверлей по ?» паттерна в доках Avalonia не найдено (⚠️ не проверено внешне). Рекомендуемая сборка из проверенных частей: `Window.KeyBindings` (проверено) + OverlayLayer (`OverlayLayer.GetOverlayLayer` — упоминается в breaking changes 12 как живой путь) либо простой полноэкранный Grid поверх (IsVisible-биндинг) + `KeyboardNavigation.TabNavigation`/`FocusManager` для обхода списка хоткеев (docs/input-interaction/focus — не читался глубоко, ⚠️).
- Местный факт: хоткеи сейчас невидимы пользователю (research 02 §2 — «видимость нулевая», редактора нет, `HotkeyConfig.Load` работает, дефолты: Space→play, Escape→pause, Ctrl+Shift+P→panic, Ctrl+N→next, Ctrl+R→replay, Ctrl+L→lock — HotkeyConfig.cs:17-25). Данные для тултипов/help-оверлея уже есть в одном месте.
- Риск: смена жестов в hotkeys.json не подтянет тултипы, если они статические — либо генерировать тултипы из HotkeyConfig при старте, либо принять, что дефолтный набор отображается статично (решение спеки).

## 6. Локальные факты: ViewModel→View и PlaybackMonitor

- **MVVM**: CommunityToolkit.Mvvm 8.4.2 (Directory.Packages.props:10). Шаблон: `[ObservableProperty]` поля (TransportViewModel.cs:20-36), `[RelayCommand(CanExecute=...)]` (:56-72), `NotifyCanExecuteChanged` в `OnLockedChanged` (:87-94). Ссылки на команды в AXAML: `{Binding PlayCommand}` (MainWindow.axaml:24-31).
- **Обмен VM→View**: `DataContext` окна = `TransportViewModel` (App.axaml.cs:70), вкладкам назначается вручную в конструкторе окна (MainWindow.axaml.cs:22-33). Новая вьюха получает DataContext так же — из `Compose` (App.axaml.cs:56-91).
- **Позиции**: `PlaybackMonitor` — latest-value, события `Changed(PositionSnapshot)`/`Cleared` (PlaybackMonitor.cs:14-18); публикация из render-потока с троттлингом ~10 Гц (AriaAudioEngine.cs:139-161, по research 01). Подписка-шаблон: `TransportViewModel` оборачивает monitor в `MonitorSubscription` и маршалит в UI через `SynchronizationContext.Post` (TransportViewModel.cs:49-52, 104-114, 131-134). Новый waveform-контрол получал бы позиции тем же путём: либо VM-свойство double (fraction) + AffectsRender, либо контрол подписывается сам с тем же маршалингом.
- **Peaks**: модель `WaveformPeaks` готова (25 pts/s), `SqliteWaveformStore` пишет при импорте (`LibraryViewModel.ImportAsync`, LibraryViewModel.cs:105-108), `Load` в проде не вызывается — waveform-контрол станет первым читателем (research 02 §5).
- **Чего в UI сейчас нет** (grep по src — 0): ToolTip, PathIcon, StreamGeometry, SplitView, ToggleButton, кастомный Render. Текущий UI — текстовые кнопки 96×56 (MainWindow.axaml:24-37).
- **Avalonia 12 breaking changes, релевантные редизайну** (docs/avalonia12-breaking-changes): compiled bindings включены по умолчанию; `ToggleButton.Checked/Unchecked` удалены → `IsCheckedChanged`; события Gestures перенесены на InputElement; анимации останавливаются на невидимых контролах (полезно для оверлеев); несколько dispatchers. Отдельное наблюдение: доки говорят, что пакет `Avalonia.Diagnostics` в v12 удалён (замена — AvaloniaUI.DiagnosticsSupport), но репозиторий пинит `Avalonia.Diagnostics 12.1.2` (Directory.Packages.props:9) — сборка зелёная; расхождение проверить при первом же касании UI-кода, к дизайну отношения не имеет.

## Сводка

| # | Вопрос | Рекомендация | Риск / не проверено |
|---|--------|--------------|---------------------|
| 1 | Waveform | Custom-drawn Control, `Render` + кэшированные StreamGeometry/Pen/Brush, пересборка на смену данных/resize; позиция — styled property + AffectsRender | Аллокации Push* в 12.x; WriteableBitmap-доки; интернал community-примеров |
| 2 | Drawer | SplitView Right + CompactOverlay/Overlay, IsPaneOpen в VM; анимация и light-dismiss встроены | Фокус/клавиатура в Overlay — не проверено |
| 3 | Иконки | PathIcon + StreamGeometry-ресурсы; Lucide (ISC/MIT) или Feather (MIT) | Stroke→fill конвертация глифов — главный риск, закрыть прототипом |
| 4 | Транспорт | Slider с кастомным ControlTheme, ToggleButton play/pause (:checked), ToolTip.Tip с жестом; фиксированный Grid-бар | Готового тонкого шаблона Slider в доках нет — писать ControlTheme |
| 5 | Хоткеи | Тултипы с жестом (из HotkeyConfig), help-оверлей KeyBinding «?/F1» + оверлей | Готового паттерна help-оверлея нет; «?» не парсится как KeyGesture — OemQuestion/F1 |
| 6 | Локальные паттерны | CommunityToolkit.Mvvm 8.4.2, подписка bus+monitor с Post в UI — шаблон TransportViewModel; peaks готовы, Load не вызывался | UI-слой не покрыт аллокационными тестами — добавить по образцу MixerBusTests |