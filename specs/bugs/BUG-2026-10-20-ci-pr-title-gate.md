# BUG-2026-10-20-ci-pr-title-gate: джоб pr-title роняет PR с человеческим заголовком

Техдолг/политика CI, не дефект продукта. Разбирать в отдельной сессии.

## Problem

- What happens: джоб `Test Build Release / pr-title (pull_request)` красный:
  `Error: No release type found in pull request title "Fix/marker pause seek"`.
  Предупреждения `punycode`/`url.parse` рядом — из node_modules экшена, они
  безобидные.
- Кого задевает: любой PR с заголовком не в формате `type: subject`. В
  репозитории это 6 открытых PR dependabot («Bump X from A to B» — не
  conventional) и PR #37 «Kickoff/UI rebuild 2026 10 10», который **всё равно
  смержили** с красным джобом (merge-коммит `a37ae6f`, 2026-10-12).
- Ожидание: гейт отражает намерение проекта. Релизу заголовок PR не нужен
  (ниже), значит требование «заголовок = conventional» — это гейт конвенции,
  который должен быть либо осознанным, либо снят.

## Root Cause Analysis

- Джоб добавлен в `d9abd5c` (2026-09-27, «ci: maximal GitHub Actions coverage
  for build, test and release») — в широком охвате CI, без отдельного решения
  о конвенции заголовков PR.
- Конфигурация минимальная: `amannn/action-semantic-pull-request@v5` без
  входов → проверяется **заголовок PR** по conventional-паттерну, набор типов
  по умолчанию. Коммиты ветки не проверяются.
- Релиз от заголовка не зависит: `.github/workflows/release.yml` → джоб
  `release` → `softprops/action-gh-release@v3` с `generate_release_notes: true`
  → release notes собирает GitHub из смерженных коммитов. Коммиты репозитория
  уже conventional (`fix(core): …`, `docs(bugs): …`, `chore(repo): …`).
- То есть джоб проверяет то, чего никто не просил, и блокирует и ботов, и
  обычный стиль заголовков.

## TDD Fix Plan (варианты на выбор)

1. **Убрать джоб** (рекомендую): релиз от заголовка не зависит, конвенция
   живёт в коммитах, а они проверяются человеком/ревью.
2. **Сделать advisory**: `continue-on-error: true` у шага — сообщение видно,
   джоб не блокирует мержа.
3. **Научить dependabot conventional-заголовкам**: в `.github/dependabot.yml`
   добавить в каждый `updates`-блок `commit-message: prefix: "chore(deps): "`.
   Снимает красный с бот-PR, но не снимает friction с человеческих PR.
4. **Оставить гейт и записать правило** в `AGENTS.md`: «заголовок PR —
   conventional-коммит». Меняет привычный стиль заголовков (в репозитории
   #37 и #49 — человеческие).
5. Если гейт остаётся — входы экшена: `ignoreLabels` (у dependabot PR здесь
   лейблов нет: `labels: []`, так что не поможет), `validateSingleCommit: true`
   (проверяет единственный коммит вместо заголовка, но у dependabot коммит
   тоже «Bump …»), `headerPattern` + `headerPatternCorrespondence` (свой
   разбор заголовка).

## Acceptance Criteria

- [ ] PR с человеческим заголовком («Fix/marker pause seek») не роняет CI
- [ ] PR dependabot («Bump X from A to B») не роняет CI
- [ ] Релиз собирается как раньше: `release.yml` не меняется, теги/notes на месте
- [ ] Коммиты ветки остаются conventional (правило в `AGENTS.md`)
- [ ] Полный локальный прогон `dotnet test` зелёный, 0 warnings

## Resolution

<!-- не начато -->
