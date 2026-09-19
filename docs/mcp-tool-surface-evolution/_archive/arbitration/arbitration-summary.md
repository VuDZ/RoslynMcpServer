# Арбитраж review MCP tool surface evolution

Дата: 2026-09-19. Предмет: пять findings из [`review/`](../review/README.md) и ответы из [`response/`](../response/README.md). Это решения для следующей ревизии плана, не реализация Stage 0/2 и не спецификация v2.

Приоритет источников: исходная задача, доступная через [`roslyn_mcp_tool_review.md`](../roslyn_mcp_tool_review.md) и описание краткого запроса в [`review-mapping.md`](../proposal-v1/review-mapping.md); явные ограничения серии в [`README.md`](../proposal-v1/README.md); затем проверяемый код и [`ARCHITECTURE.md`](../../../ARCHITECTURE.md). Исходный краткий текст чата отдельно в дереве отсутствует; его пересказ не используется для вывода новых требований.

| Finding | Позиция Grok | Позиция Astra | Итог | Обязательное изменение |
| --- | --- | --- | --- | --- |
| E0-01 | `global.json` отсутствует; определить источник SDK | Частично согласна; выбрать `10.0.x` в workflow | **ACCEPT WITH MODIFICATION** | Убрать ссылку на отсутствующий `global.json`; явно устанавливать .NET 10 SDK в CI и проверять gate на чистом runner. Создание `global.json` не требуется. |
| E2-01 | `SymbolKey` 5.9.0 непубличен; выбрать реализуемый ID | Согласна; session-scoped handle на публичных API | **ACCEPT WITH MODIFICATION** | Снять обязательность `SymbolKey`; определить непрозрачный ID в рамках load-сессии, контекст проекта и правила инвалидирования. Не предписывать checksum как доказательство идентичности. |
| E2-02 | Optional ID не снимает старые required поля | Согласна; ослабить addressing required | **ACCEPT WITH MODIFICATION** | ID-only для пяти старых tools и новые режимы только там, где они обещаны; старые вызовы валидны. `newName` остаётся required. Смешанные селекторы отклонять. |
| E2-03 | Ключ без проекта и путь linked-файла неоднозначны | Согласна; ProjectId и candidates | **ACCEPT WITH MODIFICATION** | Проектный контекст обязателен; location при нескольких memberships возвращает кандидатов. Для записи в linked-файл нужен отдельный preflight (NEW-ARB-001). |
| E2-04 | После sync ID локальной функции может указать на другую | Согласна; locals fail-closed, named + checksum | **ACCEPT WITH MODIFICATION** | При изменении документа объявления выдавать `stale-id`; для Stage 2 принять строгую инвалидацию, включая именованные декларации. |

Пять findings подтверждены по существу. Принятые автором варианты реализации не все достаточны для заявленной точности; границы решений раскрыты в [`verdicts.md`](verdicts.md). Новый существенный риск: [NEW-ARB-001](verdicts.md#new-arb-001) — физическая запись linked-файла с несколькими проектными контекстами.

Результат для стадий: Stage 0 может быть уточнён независимо. Stage 1 не затронута. Stage 2 нельзя передавать в реализацию до внесения [`normative-change-set.md`](normative-change-set.md). Stage 3 остаётся заданием на будущую спецификацию.
