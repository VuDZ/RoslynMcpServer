# Эпоха 3. События для актуальности сборок и восстановление

Статус: **готово**. Требует эпохи 2.

## Цель

Выдать потребителю build freshness независимые изменения входов и состояние
полноты покрытия. Семантический flush не очищает build dirty state.

Форма API — pull provider неизменяемого
[общего снимка](input-state-contract.md), а не вызов consumer из callback.

## Работа

- Использовать единую session generation владельца watchers/WPF-снимка
  и монотонные revisions проектных входов. Отдельный session counter не вводить.
  Путь сообщает все project instances-владельцы. Потребитель вычисляет
  обратные зависимости, а не приписывает файл единственной папке проекта.
- Generated intermediates/outputs производящего build исключаются из его
  input revisions. Это не глобальное исключение пути: подтверждённая другая
  input role учитывается отдельно. Output changed/missing может обесценить
  результат сборки отдельным сигналом; не превращать этот сигнал в input edit
  того же build. Неизвестная роль/производитель означает unknown.
- Own writes сообщать после фактически успешной persistence. Частичная запись
  отмечает реально сохранённые пути. Временное подавление watcher echo не
  заменяет уведомление и не скрывает несовпадающую внешнюю запись. Протокол
  сравнения байтов задан ниже; `TickCount`-окно не используется как доказательство.
- Публиковать coverage unknown при overflow, startup failure, утрате
  наблюдаемого каталога и недоступных входах; приводить причину и affected scope.
  Если scope неизвестен, неопределённость распространяется на весь граф.
- Rename каталога внутри membership region тоже переводит coverage в unknown.
  Возврат в полное покрытие разрешён только после новой оценки membership и
  imports: reload либо тот же источник evidence, который принят в эпохе 1,
  затем пересборка карты и подтверждение требуемых подписок. Проверка содержимого
  только известных путей не является такой оценкой. Failed/cancelled evaluation,
  недоступный input и ошибка watcher сохраняют unknown.
- При неподтверждённых условных входах другого TFM unknown распространяется
  на весь граф. Перечитывание файлов выбранного внутреннего TFM это не снимает.
- Сохранять событие, пришедшее во время flush/build. Graph reload, reset,
  смена load key и disposal завершают старую generation; поздний callback
  не может очистить/загрязнить новую сессию как событие её старого индекса.
- Предоставить consumer API для [test-build-freshness](../../test-build-freshness/README.md),
  возвращающий generation, snapshot/membership revisions, revisions путей,
  всех owners/roles, pending state, coverage и причины unknown. Pull не вызывает
  build/reload, не потребляет изменения и не захватывает manager semaphore повторно.
- Хранить significant input changes C# и не-C# в session state до появления
  consumer. Допустима агрегация revisions вместо бесконечного callback log;
  потеря owners/history означает unknown с причиной. Semantic flush и отсутствие
  подписчиков ничего не очищают. Новый consumer не получает «тишину» вместо
  XAML/import edit, случившегося до первого тестового запроса.

## Протокол собственных записей

Единица сравнения — точные байты реально успешной persistence, включая encoding
и BOM, а не только нормализованный текст. Запись хранит canonical path,
session/generation, write revision и снимок сохранённого payload (либо его
content hash/длину с проверкой устойчивости чтения). Hash не доказывает автора.

1. До I/O зарегистрировать pending write и его generation. Событие, пришедшее
   до подтверждения успеха, остаётся pending; его нельзя заранее признать echo.
2. После успешной persistence зафиксировать снимок именно записанного payload
   и отдельное уведомление о значимом input change всем владельцам. Частичная
   операция фиксирует только saved paths; failed writes не получают echo evidence.
   Учесть каждый production writer и повторную запись в `TryApplyChanges`.
3. Callback ставит путь и event revision в очередь. FSW сообщает путь/тип,
   но не содержимое в момент события — см.
   [FileSystemEventArgs](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemeventargs).
   Обработка очереди стабильно читает текущие байты и сравнивает их с committed
   snapshot своей generation; блокирующее чтение не переносится в callback.
4. Равные байты при отсутствии более нового pending события позволяют убрать
   повторную content-работу: own-write notification уже было. Несовпадение —
   новое изменение. Missing/ошибка/нестабильность чтения оставляют pending/unknown.
   Delete/Rename и membership changes не подавляются совпадением текста.
5. Событие новее начала reconciliation не очищается старой проверкой.
   Обнаруженная последовательность несовпадение → возврат к прежним байтам
   тоже не теряет revision. Простое равенство байтов не доказывает отсутствие
   промежуточных изменений, потерянных FSW; coverage/validation policy остаётся
   самостоятельной и не заменяется этим протоколом.
6. Reset/reload завершает старые write records. Сравнение не использует снимок
   другой generation; time-window разрешено только для scheduling, без права
   отбросить релевантное событие по числу прошедших тиков.

## Приёмка

Проверить внешний edit после own write, partial write, echo, overflow,
ошибку создания одного watcher, изменение во время flush, reset/reload с
поздним событием старой generation и отмену. Уведомления о не-C# входах не
теряются при semantic sync. Coverage unknown никогда не превращается в
полное покрытие простым очищением очереди.

Дополнительно проверить собственную запись A и внешнюю B в прежнем tick-окне:
B сохраняет dirty независимо от задержки события. Событие до commit, совпадающий
echo, BOM-only изменение, failed/partial persistence, нестабильное чтение,
новое событие во время reconciliation и обнаруженный ABA проходят отдельные
сценарии. Generated outputs не увеличивают input revisions производящего build.

Directory Rename → unknown → re-read известных текстов остаётся unknown;
только успешная повторная оценка membership/imports и восстановленное покрытие
снимают его. Проверить failed/cancelled evaluation и отсутствующий conditional
TFM input: последний даёт unknown на весь граф.

Проверить XAML/AdditionalFile/import edit без consumer, затем semantic flush
и первый pull: revision/роль/unknown причина сохранились. Два consumer читают
одинаковый snapshot; чтение одного не очищает состояние для другого.
Pending content event препятствует reuse до reconciliation. Generation совпадает
у WPF-проверки, input provider и build proof; callback старого watcher после
reload не изменяет новый snapshot. В тесте отсутствует callback consumer на
watcher-потоке и второй независимый input registry.

Отдельно записать предел доверия FSW: ошибка известна не при каждой потере
события. Политику проверки перед пропуском build выбирает потребитель;
эта эпоха не обещает абсолютную свежесть только по отсутствию callbacks.

## Завершение серии

Сверить контракт README, описать фактическое покрытие и обновить runtime docs
при выпуске. Передать результат build-freshness серии и будущему workspace
cache как общую модель входов; второй несовместимый индекс не вводить.
