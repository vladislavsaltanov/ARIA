# Ресерч 03: прецеденты сценария оператора

Закрывает тикет [03-research-operator-script-precedents.md](../issues/03-research-operator-script-precedents.md). Веб-исследование внешних источников; код не писался. Даты обращения к источникам — 2026-09-10.

## Метод и честные оговорки

- Проверенные первоисточники: qlab.app (документация QLab 5), Wikipedia (QLab, Cue (theatrical), Prompt book), bcitnews.com (Inception rundown, TV), lasso.io (Shoflo Rundown, live events), docs.slack.dev, docs.github.com, notion.com/help, css-tricks.com.
- **MultiPlay и Stage Research SFX проверить не удалось**: их сайты недоступны из среды, а поисковые системы (DuckDuckGo, Bing, Mojeek) закрыты капчами или выдают мусор. Ниже они упомянуты только там, где это общезвестная классификация («cue-плеер класса QLab»), без непроверенных деталей.
- **Spotify/AIMP**: официальные страницы недоступны (404 из среды). Раздел про них опирается на общепродуктовое поведение и на то, что сами QLab-docs явно противопоставляют Spotify/Apple Music театральной парадигме — это проверяемая цитата.

## 1. QLab (Figure 53): cue lists, cue carts, GO

Источники: [QLab 5 Manual](https://qlab.app/docs/v5/), [Cues](https://qlab.app/docs/v5/fundamentals/cues/), [Cue Lists](https://qlab.app/docs/v5/fundamentals/cue-lists/), [Cue Carts](https://qlab.app/docs/v5/fundamentals/cue-carts/), [Wikipedia: QLab](https://en.wikipedia.org/wiki/QLab).

**Строка = cue — базовая единица действия.** Один cue вызывает одно событие; типы (Audio, Video, Memo, Fade, Start/Stop/GoTo...). Каждый media-cue требует **target** — файл на диске; cue хранит все настройки у себя, не в файле (non-destructive: «Any changes made to a cue are saved as part of the cue, and not the media file itself» — [Wikipedia](https://en.wikipedia.org/wiki/QLab)). Это ровно пара «Track (файл/библиотека) → cue/PlaylistEntry (использование с настройками)» из языка ARIA.

**Cue list — линейный документ с playhead и GO.** «A cue list is a linear collection of cues, ordered visually from top to bottom. When a cue in a cue list receives the instruction to [go], it starts playing and the playhead moves to the next cue». У строки есть два независимых текста: **cue number** (`1`) и **cue name** (`"Preshow announcement"`) — номер-порядок и человеческая нотация разведены.

**Memo cues — прецедент свободного текста внутри документа исполнения.** «**Memo** cues are placeholders which have no effect when played. They can be used to provide in-line notes to the person operating QLab» — то есть QLab прямо поддерживает строки, которые ничего не играют и существуют только для оператора.

**Два способа исполнения — список и «тележка».** Cue list — последовательное исполнение по GO. **Cue cart** — сетка «Cue carts have no playhead and no concept of ordering; a cue cart is meant to make it easy to play cues in any order you like, and to play individual cues as many times as you like» — это модель «кликнул в строку → сыграло», ARIA-овский клик по строке сценария.

**GO-парадигма и что видит оператор.** По умолчанию cue запускается большой кнопкой GO или пробелом; на списке стоит playhead, отмечающий *standing by* cue ([Cues](https://qlab.app/docs/v5/fundamentals/cues/)). Wikipedia формулирует суть: designer программирует и блокирует файл, «an untrained user can run the software in a playback situation» ([Wikipedia](https://en.wikipedia.org/wiki/QLab)). И прямое противопоставление consumer-плеерам в доке: «If you're accustomed to using a non-theatrical playback program such as Apple Music or Spotify, this behavior can be disorienting at first. Fear not, for this is how QLab is meant to work» ([Cues](https://qlab.app/docs/v5/fundamentals/cues/)) — плейлист Spotify и cue-список QLab демонстративно разные парадигмы.

**Время: абсолютный timecode — внешний sync, не суть строки.** Cue list умеет синхронизироваться с входящим MTC/LTC: у каждого cue timecode-триггер (абсолютная точка эфирных часов), а на список — правила «On Start» с lookback (что делать, если timecode включился посреди шоу: не стартовать пропущенные кью, стартовать «за последнюю минуту/час», кастомный lookback, всё что «должно уже кончиться» — не стартовать) ([Cue Lists → The Timecode Tab](https://qlab.app/docs/v5/fundamentals/cue-lists/)). По умолчанию же время шоу порождает человек кнопкой GO.

## 2. Театр и ТВ: рабочие документы оператора

### Суфлёрский/SM-документ (prompt book)

Источник: [Wikipedia: Prompt book](https://en.wikipedia.org/wiki/Prompt_book).

Prompt book («the bible») — копия сценария, в которую сведены все блокировки, реплики, свет, звук, реквизит; ведёт её stage manager. Ключевое для ARIA:

- **Cue живёт в полях рядом с текстом сценария**: «Markings to the script (for cues, notes, etc.) are typically done in pencil, and either in the margins or on the blank side of the back of the opposing page». Документ — текст, действия — пометки у строк текста.
- **Стандарта нет, у каждого свой**: «there is no official standard, and individual stage managers will determine the best way of keeping books» — индустрия сознательно живёт со свободной нотацией, а не со строгой схемой.
- Исторически prompter («суфлёр») не только подсказывал актёрам, но и «giving cues for music and scene shifts» — музыкальный номер привязан к моменту пьесы, а не к часам.

### Нотация вызова кью (warning / standby / GO)

Источники: [Wikipedia: Cue (theatrical)](https://en.wikipedia.org/wiki/Cue_(theatrical)).

- Три уровня: **Warning → Standby → Go**; слово «go» произносится последним и является командой. Время в этой парадигме не произносится вовсе — время создаёт событие сцены.
- **Нумерация с местами для вставок**: «37, 37.3, 37.7 or 51A, 51B, 51C» — документ редактируется до и во время шоу, схема нумерации заранее оставляет щели.
- **Cue sheet** — форма с «execution, timing, sequence, intensity, volume»; master-версия живёт в prompt book у SM, у каждого цеха — свой срез. То есть: единый документ, ролевые проекции — устоявшийся паттерн.

### ТВ rundown (Inception, BCIT manual)

Источник: [BCIT Journalism Manual — Timing and Playout](https://bcitnews.com/manual/timing-and-playout/).

TV-rundown — ближайший структурный родственник «сценария с таймкодами»:

- **Soft Time vs Hard Time**: Soft — автоматическая оценка длительности по тексту пункта («calculated by Inception, of how long it will take an anchor to read the script»), меняется вместе со сценарием; Hard — оператор вбил время вручную, оно «overrides the soft time estimate» и больше не обновляется. Это две «природы» времени строки: расчётная vs закреплённая.
- **Live-исполнение документа**: кнопка **Start Playout**, затем продюсер двигает timing bar кнопкой **«Take Next Story»** — время строки фиксируется не заранее, а по факту нажатием. Окно тайминга показывает четыре времени: **Clock** (время суток), **Program time** (осталось до конца эфира), **Story time** (осталось в текущем пункте), **Over/Under** (отставание/опережение плана).
- **Actual Time**: Inception «stores a memory of how long you sit on each line» — фактические времена строк накапливаются в отдельной колонке по ходу шоу и перед следующим показом сбрасываются (Reset).
- **Floating content** — пункт можно снять с эфира/промтера без удаления из документа (строка розовеет).

### Rundown для live events (Shoflo → LASSO)

Источник: [LASSO: Rundown](https://www.lasso.io/rundown/).

- Строка = элемент с **duration**; «the system automatically calculates show timing and adjusts in real-time as the event progresses. Changing duration or start time triggers automatic recalculation» — абсолютные времена строк выводятся из порядка + длительностей, а не вводятся построчно.
- **Редактирование во время шоу** — штатный режим: «Make real-time changes mid-show... One edit updates across every connected device».
- **Show Caller Tracking**: «syncs crew devices with the producer, allowing the crew to follow the show caller's position in real-time. Track over/under time and item run time» — позиция «где мы сейчас» — первоклассное состояние документа.
- **Global Elements Manager**: повторяющиеся сегменты (заставки, спонсорские читки) редактируются один раз и подставляются в любой rundown — аналог «переиспользуемых элементов» над строками.

**Общий вывод по разделу:** везде «время» и «элемент программы» связываются через **порядок + триггер** (go / Take Next / show caller), а абсолютное время — производное: план (расчёт от длительностей) или внешний sync (LTC/MTC). Факт пишется кнопкой-действием во время шоу.

## 3. Spotify/AIMP: почему плейлист — не сценарий

Официальные страницы в среде недоступны; ниже — общепродуктовые наблюдения плюс проверяемая цитата из QLab-docs (см. §1).

Плейлист Spotify/AIMP — **контейнер воспроизведения**: порядок строк сам является порядком автовоспроизведения (кончилась строка — началась следующая), строка ничего «не делает», она просто играет. В рабочем документе оператора строка — **действие**: она имеет статус (стоит/исполнена), срабатывает по явному GO/клику, и между её исполнениями документ может свободно редактироваться, плыть по времени (Over/Under), а порядок и реальность могут расходиться (float, взятия с середины). Именно это QLab называет «disorienting» для привычных к Spotify-пользователей: там нет playhead-standby и нет GO.

**Что у consumer-плееров переносимо в панель сценария:**

- строка с длительностью (duration column) — привычная, полезна и в сценарии как ориентир;
- hover-действия на строке (кнопки play/меню появляются при наведении — их UX-приём);
- drag&drop переупорядочивание строк;
- вторичная типографика метаданных (серый мелкий текст — артист/альбом) — пригодится для отображения имени Track в упоминании.

Не переносится и не нужно копировать: автоплей «следующей строки», позиция строки как единственный источник порядка воспроизведения, отсутствие «исполнено/стоит».

## 4. Паттерны @упоминаний

Источники: [Slack: Formatting message text](https://docs.slack.dev/messaging/formatting-message-text), [GitHub: Basic writing and formatting syntax](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax), [Notion: Comments, mentions & reactions](https://www.notion.com/help/comments-mentions-and-reminders), [CSS-Tricks: @mention autocomplete](https://css-tricks.com/so-you-want-to-build-an-mention-autocomplete-feature/).

**Синтаксис и резолвинг имени → идентификатор:**

- **Slack хранит ID, а не имя**: текст сообщения содержит `<@U012AB3CD>`; «the ID will be automatically converted to show the display name of the user» — имя подставляется при **рендере**, в хранимом тексте его нет. Обоснование Slack прямо говорит, что переносить на треки: «the names of conversations or user groups may change at any time. What was previously a functioning reference may no longer work. Meanwhile, an ID will always remain the same». Автоматический парсинг по именам пользователей Slack **deprecated** (changelog 2017) именно из-за переименований.
- **Notion хранит ссылку на страницу**: «If you change the title of a page, the new title will automatically reflect that change wherever the page is @-mentioned» — упоминание переживает переименование цели, поэтому его не надо чинить. Плюс автоматически создаётся backlink (можно найти все строки, где трек упомянут).
- **GitHub ссылается по username** и дополнительно по стабильному номеру: `#` открывает список issues с фильтрацией по номеру/названию — номер всегда стабилен, имя нет.

**Автокомплит по `@`:**

- GitHub: «Typing an @ symbol will bring up a list of people or teams on a project. The list filters as you type... use the arrow keys to select it and press either tab or enter to complete the name»; список ограничен участниками проекта.
- Notion: `@` открывает меню, которое «will search for them in real-time» — один триггер ищет людей, страницы и даты (группировка по типу — мышечная память, [CSS-Tricks](https://css-tricks.com/so-you-want-to-build-an-mention-autocomplete-feature/)).
- CSS-Tricks описывает механику UX-деталей: панель закрывается по эвристике «вышел из токена» (Twitter — по пробелу; Slack разрешает пробелы и решает иначе); при выборе «closes the panel, replaces the token, and adds a space»; вставленное упоминание **остаётся интерактивным** — клик или стрелки возвращают панель с упоминанием в качестве запроса, чтобы его отредактировать.

**Поведение при удалении референса:** все системы синхронно показывают «повисшую» ссылку: Slack рендерит невалидного пользователя как некликабельную заглушку, GitHub превращает несуществующие упоминания в обычный текст, Notion пишет «Deleted page». Ключевой инвариант: хранимый идентификатор не ретирится, деградирует только отображение. Для ARIA это значит: mention хранит `TrackId`; если Track удалён/Faulted — строка остаётся, ссылка рисуется приглушённо и не исполняется.

## 5. Таймкод строки: абсолютный или относительный

Сводка по прецедентам:

| Инструмент/практика | Что проставлено в строке | Как появляется |
|---|---|---|
| Театр (prompt book, cue calling) | Ничего: порядок + реплика-триггер | Строка привязана к моменту сценария; «GO» создаёт время |
| QLab | Опционально абсолютный timecode (MTC/LTC) на cue | Внешние эфирные часы + lookback-правила; по умолчанию — ручной GO |
| TV rundown (Inception) | Soft (расчёт) / Hard (закреплён) длительности; абсолютные Clock/Program/Over-Under — производные окна | План считается от стартового времени; факт пишется кнопкой «Take Next Story» в Actual Time |
| Shoflo/LASSO | Длительность на строку | «the system automatically calculates show timing and adjusts in real-time as the event progresses» |
| Упоминания в редакторах | n/a | Дата-упоминания (`@today`) — единственный «временной» тип mention (Notion) |

Выводы для ARIA:

1. **Ни один прецедент не делает абсолютное время суток первичным полем рабочей строки.** Первичны либо порядок+триггер (театр, QLab), либо длительность+расчёт (rundown). Абсолютное время — либо план, либо внешний sync, либо производная проекция.
2. **«Пометить сейчас» — штатный паттерн**, в двух видах: движение playhead/timing bar с записью факта (Inception «Take Next Story» + Actual Time) и синхронный трекинг позиции show caller'а (Shoflo). Это соответствует ответу пользователя из карты: документ редактируется по ходу шоу, время проставляется кнопкой.
3. **Относительное время** в строке осмысленно как *elapsed от начала шоу* (факт, пишется «пометить сейчас») и/или *расчёт от длительностей* (план, rundown-модель). Относительный таймкод «от начала трека» в прецедентах не встречается — смещение внутри файла уже выражено в домене ARIA через CuePoint/Override, тащить его в таймкод строки — дублирование.

## Модели строки сценария

Общее для всех вариантов (из прецедентов): строка — элемент линейного документа; исполнение строки = действие оператора (клик/GO), а не автоплей; ссылка на трек хранится идентификатором, отображается именем на момент рендера; удаление трека не убивает строку.

### Вариант A — «Memo-строка» (свободный текст, трек внутри текста)

Строка = сплошной текст; упоминания Track — инлайновые «острова» внутри текста (как @mention в Slack/Notion). Структурных полей, кроме текста и позиции, нет. Клик по строке исполняет **первое** упоминание строки.

- Поля: `lineId`, `orderPosition`, `text` (rich: `TextRun | TrackMention(trackId)`), опционально `markedAt`-время.
- Плюсы: максимально близко к prompt book и Notion — документ читается как документ; нулевой порог входа; любые пометки/ремарки без schema-change; переименование трека автоматически отражается (рендер по имени).
- Минусы: неоднозначность при нескольких упоминаниях в строке (что ставить в Queue — первый? ближайший к клику?); клик «по строке» размывается до клика «по слову»; время если и хранить, то непонятно, к чему оно относится (строке? треку?).

### Вариант B — «Строка-кью» (всё структурировано, модель QLab/rundown)

Строка = запись с обязательными полями: номер/порядок, нотация (текст), ссылка на Track (или PlaylistEntry), таймкод, способ запуска. Текст без упоминания трека — допустимая «Memo-строка» с пустым target (как Memo cue в QLab).

- Поля: `lineId`, `number` (37, 37.3…), `notation`, `trackId?`, `timecode?`, `trigger` (click|go|none).
- Плюсы: однозначность исполнения (один target на строку — правило QLab «cues... can only ever have one target at a time»); таймкод — честное поле, можно планировать/сортировать; совместимо с будущим автозапуском (Trigger-шов) и с экспортом в rundown-формат.
- Минусы: это уже программирование шоу, а не рабочий документ: оператор вынужден заполнять схему; свободный текст второго сорта; дублирует структуру Playlist/Queue в another place — риск конкуренции с плейлистом вместо дополнения; для конференций (текст-заметки оператора) избыточно.

### Вариант C — «Строка-документ со структурным якорем» (гибрид: текст Notion-стиля + rundown-время)

Строка = свободный текст, в котором упоминания Track — полноценные ссылки по `trackId` (рендер по имени, hover-подсветка, автокомплит по `@`); плюс отдельное структурное поле времени строки. Кли по строке ставит в Queue **упомянутый трек; если в строке несколько упоминаний — выделенный/последний добавленный, а не «первый попавшийся»** (уточняется тикетом 07 по домен-модели).

- Поля: `lineId`, `orderPosition`, `runs: TextRun | TrackMention(trackId)`, `time?: ShowTimeMark` — отметка, записанная кнопкой «пометить сейчас» (elapsed от начала Show), отображаемая как абсолют (время суток) или как elapsed — по настройке таймера из карты; при желании — расчётный «плановый» тайминг от длительностей упомянутых треков (rundown-модель Shoflo/Inception).
- Плюсы: совпадает со всеми проверенными прецедентами одновременно — свободная нотация как prompt book/Memo cue; упоминание-ссылка как Slack/Notion (ID стабилен, имя рендерится, переименование бесплатно); время как «факт кнопкой» как Inception, абсолют/elapsed — проекция отображения, как Clock vs Program time; клик по строке сохраняет простоту исполнения.
- Минусы: нужно аккуратно определить привязку `time` (к строке, не к треку) и поведение при нескольких упоминаниях; чуть дороже в реализации рендера (инлайновые runs, а не plain text).

## Рекомендация

**Вариант C** как основа модели строки сценария. Он единственный не жертвует ни одной функцией прецедентов: документ остаётся свободным текстом оператора (главный способ существования таких документов — prompt book с «no official standard»), исполнение остаётся однозначным (упоминание — структурная ссылка по `TrackId`, а не парсинг текста), а время не становится ложным планом: оно пишется оператором кнопкой «пометить сейчас» и хранится как elapsed, абсолютное время — производная отображения. Терминология для CONTEXT.md при этом естественна: строка сценария — **ScriptLine**, ссылка на трек внутри строки — **TrackMention** (ссылка на Track библиотеки, не на PlaylistEntry: сценарий описывает «что сыграть», Queue/плейлист уже определяют «как продолжится воспроизведение» — это домен Queue-шва; PlaylistEntry-подобные переопределения — предмет тикета 07).

Ключевые переносимые паттерны UX: автокомплит по `@` с фильтрацией по мере ввода и Tab/Enter (GitHub); упоминание как интерактивный объект, открывающий панель правки при клике (Twitter/Slack, CSS-Tricks); hover-подсветка упоминания; невалидная ссылка деградирует до приглушённой плашки, а не удаляет строку (Slack/Notion); отсутствие playhead как обязательного элемента — клик по любой строке независим (QLab cue cart «no playhead... play in any order»).