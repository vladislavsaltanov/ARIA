# BUG-2026-10-21-remote-robustness: пульт нагружает Control-поток и копит память

## Problem

- Actual:
  - `OnStateEvent` (подписчик шины, исполняется на Control-потоке) сериализовал полный JSON `ShowState` (все проекты и скрипты) на каждую дельту, даже когда пультов нет. Пока идут часы шоу, это происходит каждую секунду.
  - Исходящая очередь соединения не ограничена: уснувший планшет с живым TCP копит кадры позиции и дельты всё шоу.
  - Входящее сообщение собиралось `StringBuilder`'ом по фрагментам с декодированием UTF-8 каждого фрагмента отдельно. Кириллица на границе фрагмента превращалась в `U+FFFD`, а размер сообщения ничем не ограничивался.
  - `seq ≤ 0` от клиента доходил до `CommandBus.Submit`, получал `ArgumentOutOfRangeException` мимо фильтра catch и рвал соединение.
  - Исключение в цикле позиции/LUFS навсегда останавливало рассылку.
- Expected: без подключений нет работы на Control-потоке; медленный клиент отключается (переподключится и получит снимок); кадры декодируются целиком; лимит 1 МБ на сообщение; плохой `seq` отклоняется.

## Fix

- `OnStateEvent`: ранний выход при `_connections.IsEmpty`.
- Исходящий канал bounded (2048); переполнение отменяет `Lifetime` соединения.
- Приём в `MemoryStream`, декодирование целого сообщения, `MessageTooBig` больше 1 МБ.
- `seq ≤ 0` → `rejected`/`bad-seq`.
- Тело тика позиции/LUFS изолировано.

## Acceptance Criteria

- [x] `RemoteRobustnessTests.NonPositiveSeq_IsRejected_AndConnectionSurvives`
- [x] `RemoteRobustnessTests.FragmentedMessage_SplitInsideCyrillicChar_IsDecodedIntact`
- [x] `RemoteRobustnessTests.OversizedMessage_ClosesConnection_WithoutCrashingHost`
- [ ] Отключение медленного клиента: юнит-тестом не покрыто (буферы loopback-TCP поглощают мегабайты; детерминированный тест требует инъекции сокета)
