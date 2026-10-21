# BUG-2026-10-21-snapshot-race: Snapshot() с чужого потока читает изменяемое состояние контроллера

## Problem

- Actual: `CommandBus.Snapshot()` напрямую вызывал `ShowController.Snapshot()` с любого потока: UI (хоткеи, VM), таймер автосейва, Remote. Снимок перечисляет `List<QueueItem>` и `HashSet<TrackId>`, которые Control-поток в этот момент мутирует. Отсюда `InvalidOperationException: Collection was modified` на UI-потоке (а это падение приложения) и рваные снимки в автосейве. `ShowController.TrackAudio/TrackBpm` читали `Dictionary` с UI-потока во время записи.
- Expected: чужие потоки получают целостный неизменяемый снимок и не трогают живое состояние.

## Root Cause Analysis

- Инвариант «PlayerState мутирует только Control-поток» соблюдался для записи, но чтение снимка не было опубликовано.
- Risk level: High: редкая, но реальная авария на хоткее посреди шоу.

## Fix

- В Pumped-режиме шина публикует снимок (`Volatile`) на Control-потоке: перед рассылкой каждого события (подписчик, читающий `Snapshot()` после дельты, видит эту дельту) и после каждой команды. `Snapshot()` с любого потока возвращает опубликованный снимок. Inline-режим не изменился.
- `TrackAudio/TrackBpm` ищут по неизменяемому `_tracks`.

## Acceptance Criteria

- [x] `Snapshot()` во время исполнения команды не вызывает обработчик конкурентно (`BusResilienceTests.Snapshot_FromForeignThread_NeverRacesTheControlThread`)
- [x] Снимок, прочитанный после наблюдаемой дельты, её содержит (`Snapshot_ReadAfterObservedDelta_ReflectsThatDelta`)
