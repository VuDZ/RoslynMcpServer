# Сквозные риски и согласованность решений

## Один контракт identity для чтения и записи

E2-01 выбирает session handle; E2-03 добавляет project context; E2-04 ограничивает жизнь ID после sync. Это единый resolver, который должен использоваться `get_symbol_info`, `get_symbol_source`, navigation и rename. Разные проверки в разных adapters создадут ситуацию, когда read возвращает `stale-id`, а rename меняет другой символ. Identity validation выполняется до preview и до apply; write freshness gate проверяет последующий write base, но не заменяет identity validation.

## Точность project context и физический файл

E2-03 решает неоднозначность Roslyn `Document`, но один physical path может принадлежать нескольким проектам. NEW-ARB-001 требует согласованного результата для всех memberships до первой записи. Особенно важны `rename_symbol(scope="project")` и linked source: проектный scope Roslyn не делает физический файл приватным. Текущий [`PersistDocumentChangesAsync`](../../../../Services/SolutionManager.cs) пишет по документам, поэтому без общего preflight возможны разные тексты по одному пути и последний писатель определит результат.

## Совместимость схемы и адресование

E2-02 ослабляет required у прежних полей, сохраняя прежние валидные запросы. Документация, JSON Schema и runtime должны вместе выражать режимы; одна лишь optional сигнатура не обеспечивает обязательность `newName` и не запрещает пустой selector. MCP schema может не выразить взаимоисключающие режимы; runtime обязан это делать, а протокольные тесты проверять фактические вызовы. ID и legacy поля в одном запросе отклоняются, чтобы их несоответствие не выбирало скрытый приоритет.

## Стабильность и наблюдаемость

Слово «stable» из входящего review означает повторное использование в оговорённом lifetime, а не сохранение после любых правок. `stale-id`, `conflicting-selectors`, `missing-selector` и `shared-path-conflict` должны быть различимы для клиента. Возврат candidates нужен при неопределённом имени или project membership, но никогда после провала проверки исторической идентичности ID.

## Порядок стадий

E0-01 не зависит от Stage 2. Stage 1 переносит tool membership без изменения схемы. Изменение `required` и новые tools в Stage 2 — minor 1.x по принятому плану, но это всё равно изменение опубликованной JSON Schema; провести schema/call tests перед minor release. Stage 3 не должен наследовать непубличный `SymbolKey` или считать Stage 2 гарантирующей переносимый ID.
