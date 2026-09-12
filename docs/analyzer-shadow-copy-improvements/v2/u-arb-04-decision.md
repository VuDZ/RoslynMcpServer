# U-ARB-04 — semantic/load boundary

Дата: 2026-09-12. Статус: **выбрана, реализована и
[принята](u-arb-04-implementation-acceptance.md) в v1.3.14**. Спецификация:
[u-arb-04-atomic-load-prepare.md](u-arb-04-atomic-load-prepare.md).
Inaccessible не открывался.

## Решение

Для `load_workspace(..., shadowCopyInSolutionAnalyzers=true)` physical
load/cache lookup, analyzer shadow prepare, execution gate и публикация итогового
snapshot должны образовывать одну линейризуемую операцию `SolutionManager`.
Новый raw snapshot этой load-сессии нельзя публиковать semantic readers между
physical load и prepare.

Production semantic readers должны получать snapshot через сериализованный
manager API, а не через lock-free `GetCurrentSolution()` в момент перехода
load/enable. Конкурентный semantic request упорядочивается целиком:

- если он вошёл до opt-in load/enable, это no-overlay operation; загрузка real
  analyzer и последующий restart-required identity gate остаются честным
  результатом;
- если load/enable вошёл первым, semantic request ждёт завершения boundary и
  получает только активный shadow overlay;
- состояния «новая сессия уже опубликована raw, prepare ещё не выполнен» для
  production semantic reader нет.

Публичный session token, история snapshots и MVCC не вводятся.

## Почему boundary нужна

Evidence уже доказал следствие опасной вставки: обычный
`GetCompilationAsync` из published `GetCurrentSolution()` без overlay загружает
existing-correct real DLL и на Windows блокирует forced rebuild (`MSB3021`).

На момент выбора решения такая вставка разрешалась реализацией v1.3.13:

1. MCP SDK 1.3.0 `McpSessionHandler.ProcessMessagesCoreAsync` запускает обработку
   каждого сообщения независимо и не ждёт завершения предыдущего handler.
2. `WorkspaceTools.LoadWorkspace` сначала ожидает `SolutionManager.LoadAsync`,
   затем отдельно вызывает `ShadowCopyInSolutionAnalyzerReferencesAsync`.
3. Оба manager API независимо захватывают `_workspaceLock`; `LoadCoreAsync`
   публикует `_solution` с raw analyzer references до освобождения первого
   захвата.
4. Между захватами другой handler может начать semantic operation. Часть
   production callers также читает `GetCurrentSolution()` без lock, поэтому
   одного объединения двух вызовов недостаточно без аудита semantic access.

Production inventory не нашла намеренного raw-workspace semantic reader, но не
устраняет эту конкурентную interleaving. Фактическая возможность dispatch
следует из SDK и текущих lock boundaries; дополнительный вероятностный repro не
нужен для выбора политики.

## Граница реализации

Минимальная реализация должна:

1. добавить единый manager workflow load/cache → optional prepare/gate →
   publish под одним `_workspaceLock`;
2. не публиковать raw snapshot новой opt-in сессии до успешной активации overlay;
3. fail closed при prepare failure: не выдавать новый raw snapshot как
   полноценный opt-in result;
4. перевести production compilation/semantic-model readers на сериализованный
   snapshot accessor и сохранить inventory guard для новых callers;
5. не менять session-sticky U-ARB-05, restart-required identity policy,
   confirmed-only matcher и write boundary;
6. добавить детерминированный concurrency test с паузой между physical load и
   prepare: semantic request не должен увидеть промежуточный raw snapshot.

Отдельно проверить cache-hit `false → true`, graph reopen и cancellation.
Test-only raw oracle остаётся только диагностическим seam и в production
boundary не входит.

## Приёмка

- При load/enable, который первым вошёл в boundary, конкурентная production
  semantic compilation наблюдает shadow path, real path отсутствует в process
  assemblies, forced rebuild меняет DLL bytes.
- При no-overlay semantic, который вошёл первым, поздний enable детерминированно
  сообщает restart-required; это не маскируется как успешная активация.
- Prepare failure/cancellation не публикует raw opt-in snapshot.
- Все прежние U-ARB-04 evidence и epoch-1 write-path тесты остаются зелёными.

С v1.3.14 boundary является shipped behavior; результаты:
[u-arb-04-implementation-acceptance.md](u-arb-04-implementation-acceptance.md).
