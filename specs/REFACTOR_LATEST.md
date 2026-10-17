# REFACTOR_LATEST — marker lane UI/UX robustness

type: refactor-plan
context: markers-seek-panel slice follow-up (perf + robustness, no behavior change)

## Problem Statement

Полоса меток на seek-панели работает, но дорога и хрупка: полный rebuild
Canvas-детей с layout-проходом на каждое движение мыши/hover/ресайз, аллокации
хит-теста на горячем пути указателя, тройная сортировка меток на пути
VM → движок → микшер, четыре копии headless-фикстур в тестах. При росте числа
меток и частых протяжках UI будет проседать; дубли фикстур уже дрейфуют.

## Solution

Три независимые волны без изменения поведения: (A) чистая геометрия полосы +
дешёвая перерисовка + хит-тест без аллокаций; (B) один канонический инвариант
«хранится отсортированным» с владельцем в VM, движок/микшер — глупые
потребители с assert; (C) общая headless-фикстура тестов. Затем микрокаты
контроллера шоу и визуальный гейт снапшотами.

## Commits

1. Чистая геометрия полосы вынесена, поведение то же → verify: `dotnet test tests/ARIA.App.Tests`
2. Хит-тест без аллокаций на пути указателя, поведение то же → verify: `dotnet test tests/ARIA.App.Tests`
3. Перерисовка без rebuild на drag/hover, поведение то же → verify: `dotnet test tests/ARIA.App.Tests`
4. Сортировка только у владельца инварианта, движок/микшер доверяют + assert → verify: `dotnet test tests/ARIA.Audio.Tests tests/ARIA.Core.Tests`
5. Общая headless-фикстура, дубли удалены → verify: `dotnet test tests/ARIA.App.Tests`
6. Микрокаты контроллера шоу, поведение то же → verify: `dotnet test ARIA.slnx`
7. Визуальный гейт: геометрия полосы и волны без сдвигов → verify: `tools/Snapshots` + `impeccable detect`

## Decision Document

- Модули: полоса меток (геометрия отдельно от Canvas-мутаций); путь меток
  VM → движок → микшер (инвариант у VM); headless-фикстуры (один адаптер).
- Интерфейсы: геометрия — чистые входы (duration, width, markers) и выходы
  (позиции); хит-тест — готовый список + x; SetMarkers — предусловие порядка.
- Clarifications: поведение (постановка/правка/удаление/Ctrl+Z/автостоп/флор)
  не меняется; Render-переход — только если волны 1–3 чисты и гейт зелёный.
- Architecture: ADR 0009 соблюдён (ShowController не дробится, только мелкие
  сокращения); ADR 0010 не пересматривается.
- Contracts: порядок меток — по Position; пустой набор — пустой массив, не
  default; хит-радиус и drag-порог неизменны.

## Testing Decisions

- Хороший тест: внешнее поведение через публичные интерфейсы
  (ICommandBus/IAudioEngine/ILibraryStore), не детали реализации.
- Модули: геометрия — новые юнит-тесты без headless; движок — zero-alloc
  тесты держать; UI — существующие headless-тесты гейтом.
- Prior art: MarkerLaneHeadlessTests, zero-alloc тесты аудио-колбека,
  tools/Snapshots как визуальный гейт.

## Out of Scope

- Разбиение ShowController на модули (запрещено ADR 0009).
- Изменение ScanCutoff (форма оптимальна, zero-alloc тест держит).
- Новые UX-фичи полосы (редактор в браузере, live re-arm, метка с клавиатуры).
- Render(DrawingContext)-переход в этой волне — кандидатом следующей, только
  по Context7-рецепту кастомного рендера при зелёном гейте.

## Further Notes

- Skills: bigpowers plan-refactor (этот план) → dispatch-agents waves →
  audit-code --gate → verify-work (снапшоты) → commit-message; superpowers
  audit-code scenarios A/B/C на каждую волну; modern-dotnet §10 на каждый
  C#-дифф; impeccable = визуальный гейт (Snapshots + detect []).
- Prior art: Context7 Avalonia hit-testing («single control Render override
  eliminates per-element hit testing»), .NET Array.Sort/Span guidance.
- AGENTS.md: полный `dotnet test` перед коммитом, 0 предупреждений, коммит =
  один логический батч + push; параллельные агенты — только с изоляцией,
  результат проверять на диске и тестами.
