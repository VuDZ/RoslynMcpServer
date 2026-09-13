# U-ARB-05 — семантика повторного флага

Дата: 2026-09-12. Статус: **выбран и реализован в v1.3.13 — session-sticky**.

## Решение

`shadowCopyInSolutionAnalyzers=true` включает или обновляет overlay текущей
load-сессии. На same-key cache hit последующий `false` или omission:

- не выключает активный overlay;
- не запускает подготовку/refresh analyzer-файлов;
- сохраняет текущие mapping, generation и результаты последней подготовки.

Отключение выполняется через `reset_workspace`, после которого workspace загружается
с `false`/без аргумента. Cache miss из-за другого load key, другого решения или
graph-stale reopen также создаёт новую сессию без переноса прежнего overlay.

## Обоснование совместимости

Публичная MCP-граница использует optional non-nullable `bool = false`; сервер не
может отличить omission старого клиента от явного `false`. Desired-state политика
поэтому превратила бы обычный повторный load без нового аргумента в неявный disable
и могла бы вернуть broken raw analyzer reference в следующую semantic operation.

Session-sticky — уже выпущенное поведение с v1.3.5 и документированный способ
отключения. Репозиторий не содержит evidence, что клиент полагается на `false` как
на disable; такого контракта не было. Сохранение текущей семантики не требует
tri-state и не меняет публичную schema.

## Наблюдаемость и проверка

Ответ cached load с `false`/omission теперь явно сообщает, что активный session
overlay сохранён и не обновлялся. Lifecycle-тест проверяет cache hit, отсутствие
prepare, неизменные prepare count/generation, активный overlay и точный marker;
после reset варианты `false`/omitted оставляют overlay выключенным.
