# Story e01s04: Headless общий session-fixture

**type:** refactor
**risk:** P1
**context:** infra

**Context**: 13 headless-классов стартуют свой
`HeadlessUnitTestSession` и гасят его в Dispose.
Старт сессии — дорогая и флакующая точка
(`DefaultRenderLoop.Add`,Abort). Один общий
fixture на коллекцию убирает 12 стартов.

## Zoom-out

- **Purpose**: одна сессия на коллекцию headless;
  классы берут её из конструктора.
- **Callers**: 13 `*HeadlessTests`/`*Tests` классов
  с полем `_session`.
- **Contracts**: паттерн uniform (`StartNew`,
  `Dispose`, `_session.Dispatch`); классы с доп.
  Dispose-логикой (temp-диры) сохраняют её.

## Requirements

#### MODIFIED: Сессия стартует один раз на коллекцию

**Before:** Каждый класс `StartNew` + `Dispose`;
13 стартов на прогон.
**After:** `HeadlessSessionFixture` держит одну
сессию; классы получают её через ctor; свой
Dispose чистят только своё (temp-диры).

## Steps

1. Fixture + wiring коллекции + 4 малых класса
   → verify: `dotnet build tests/ARIA.App.Tests`
2. Остальные 9 классов батчами
   → verify: `dotnet test tests/ARIA.App.Tests`
3. 5 прогонов подряд без флаков старта
   → verify: `for i in 1 2 3 4 5; do dotnet test
   tests/ARIA.App.Tests; done`
4. Полный прогон
   → verify: `dotnet test`

## Verification Script

1. `dotnet test tests/ARIA.App.Tests` зелен.
2. Повторить 5 раз — без Abort/VerifyAccess.
3. Время прогона не выросло.

## Out of scope

- Прод-код (только тесты).
- Параллелизация коллекции.

## Risks

- Состояние App-синглтона между классами:
  тесты изолируют окна/шины сами; гоню suite.
- P1, не P0: только тест-инфра.

## Acceptance

- [ ] Один `StartNew` на весь прогон.
- [ ] 5 чистых прогонов подряд.
- [ ] `dotnet test` зелен, 0 warnings.
