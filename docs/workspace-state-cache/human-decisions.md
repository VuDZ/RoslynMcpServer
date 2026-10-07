# Решения владельца после арбитража

Дата: **2026-10-07**. Это дополнительные normative inputs для specification v2;
исходные arbitration/review/defense не переписываются. Принятие ограниченного
contract не означает implementation acceptance или выполнения эксперимента.

## H-001 — Документация бинарных зависимостей исключена из гарантии MVP

Статус: **accepted owner decision / applied to requirements**, 2026-10-07.
Источник: прямое сообщение владельца в обсуждении U-001: «лучше исключить» после
уточнения границы между бинарными зависимостями и загруженными исходными проектами.
Применимый вопрос: XML-doc часть [U-001](unresolved.md#u-001--fidelity-положительного-e0)
из [ARB-001](archive/arbitration/findings/ARB-001.md).

### Принятый contract

- Восстановление MVP не гарантирует XML-документацию символов из metadata/DLL
  references. Это включает NuGet, framework/manual DLL и собственную библиотеку,
  если в workspace она представлена бинарной ссылкой, а не загруженным проектом.
- Capture не обязан извлекать/переносить исходный DocumentationProvider; отсутствие
  точного documentation state само по себе не делает такой reference unsupported.
  Оставшаяся metadata/reference/options fidelity и реальные positive controls обязательны.
  Ограничение обозначается в support profile/help/outcome; потеря не выдаётся за
  точную эквивалентность и не является silent default.
- Исходники загруженных проектов, их комментарии и parse options сохраняют прежний
  contract. Генерация документации при ordinary build не отключается этой cache policy.
- Изменение/исчезновение XML, используемого только как документация бинарной ссылки,
  не обязано инвалидировать cache. Расширение `.xml` не исключает файл с другой
  обязательной ролью: AdditionalFile/import/config/custom input проверяются по своим gates.
  DLL, package/restore inputs и graph identity продолжают проходить прежнюю validation.
- Запросы документации бинарных символов и analyzers/generators, чьи результаты
  зависят от неё, находятся вне supported hydrated scope. Для них применяется
  предусмотренный ordinary-load route либо явный отказ до execution/side effects;
  прежние admission gates не ослабляются.
- Acceptance сравнивает supported semantic/write outcomes, сохранённые source comments
  и metadata properties. Отдельный control проверяет заявленное XML ограничение и
  отказ/fallback documentation-dependent потребителя; успех одного symbol query не
  доказывает независимость всех analyzers/generators.

### Rationale и принятый риск

Для первого MVP владелец предпочёл полезное восстановление без точного переноса
documentation provider бинарных зависимостей. Может отсутствовать/отличаться текст
описания внешнего API даже при прежней DLL и при неизменном XML после restore.
Это сознательный предел гарантии, не assertion неизменности XML.

XML хранится отдельно: правка комментариев/перегенерация документации не обязательно
меняет DLL. Хеши csproj/NuGet.Config и DLL не доказывают неизменность документации
или правильность её provider. Поэтому H-001 не опирается на предположение
«изменение документации обязательно меняет бинарь».

### Оставшееся решение на момент H-001

На момент H-001 strong-name/signing часть U-001 была **UNRESOLVED**: разрешения терять provider search
paths или signing/diagnostic/emit behavior владелец этим решением не дал. S-001
оставался not-run и требовал остаточного fidelity решения до positive executable scope.
Точное представление DLL references/aliases/EmbedInteropTypes и ordinary fallback
при изменениях inputs не отменяются.

Применение к файлам и проверка: [change ledger](change-ledger.md#h-001--решение-владельца)
и [revision report](preparation-report.md).

## H-002 — Ограниченный strong-name contract MVP

Статус: **accepted owner decision / applied to requirements**, 2026-10-07.
Источник: прямое решение владельца в обсуждении U-001: «лучше вариант B, я крайне
редко сталкивался с подписанием сборок, а для mvp такое ограничение подойдёт,
иначе никогда не закончим».
Применимый вопрос: оставшаяся strong-name часть [U-001](unresolved.md#u-001--fidelity-положительного-e0).
Вместе с H-001 это закрывает U-001 как выбор требований, не как доказательство capability.

### Принятый contract

- Выбран **B — ограниченная observable семантика**. MVP не гарантирует точного
  переноса непубличного состояния strong-name provider, включая search paths.
  Точное извлечение этих настроек не является prerequisite поддержанного среза.
- Hydrated scope допускает только явно подтверждённые проекты и операции, для
  которых невосстановленное состояние не меняет заявленные semantic results,
  диагностику поддержанных операций и корректность поддержанных `.cs` writes.
  Конкретный finite profile и воспроизводимые controls фиксирует S-001.
- Signing-зависимый или неопределённый request использует whole-request ordinary
  load до выполнения зависимой операции и side effects. Если зависимость выявлена
  после открытия hydrated session, переход на ordinary route предшествует операции;
  при невозможности перехода возвращается явный отказ без выполнения операции.
  Unknown не становится supported, а обычная пригодная загрузка не блокируется
  только из-за неподдержанного cache scope.
- Emit и подписание сборок из восстановленной compilation вне первоначального
  MVP scope. Обычная сборка исходного проекта сохраняет свои signing settings;
  csproj, signing flags и references не переписываются ради cache eligibility.
- Недостаточно одного `SignAssembly=false`, поиска `.snk` в csproj или успешного
  symbol query. Проверки учитывают effective options после imports/conditions,
  применимые assembly attributes, project bindings и зависимости семантики от
  strong-name identity, включая `InternalsVisibleTo`. Analyzer/generator execution
  с неизвестной зависимостью не объявляется safe; существующие fresh
  preparation/admission gates сохраняются.
- Подписанная внешняя metadata/DLL reference сама по себе не исключает request.
  Её identity/properties и остальные reference facts сохраняются; оценивается
  зависимость результата от невосстановленного provider загруженных проектов.
- Конкретная public reconstruction policy явно описывается в profile/report.
  Она не называется точным переносом hidden state; private reflection и silent
  defaults для remaining mandatory state запрещены. Provider type сам по себе
  не является ни разрешением, ни безусловным отказом для signing-independent slice.
- Support profile/help/outcome и benchmark compare key отражают ограничения H-001/H-002.
  Нельзя выдавать совпадение выбранных запросов за equivalence всех diagnostics,
  analyzers/generators или emit/signing behavior.

### Явная диагностика в логах

Дополнительное требование владельца: «это должно писаться в логах явно, чтобы было понятно».
Cache bypass/capture skip/fallback/refusal по H-002 обязательно имеет явную запись
в обычном диагностическом логе, не только trace/debug. Одного `cache miss` недостаточно.
Запись содержит стадию (capture/lookup/operation), затронутый project instance и
configuration/TFM по доступному evidence, запрошенную операцию, конкретную причину
(signing-dependent, signing-state-unknown либо excluded emit/signing) и выбранный
исход (ordinary load либо refusal). Если binding неизвестен, это отмечается явно.

Сообщение различает ограничение cache profile и реальную ошибку ordinary load,
например недоступный ключ: fallback не заявляется успешной ordinary загрузкой до
её результата. Signing, включённый только в CI, не логируется как причина отказа
локального profile без actual evidence. Логи не содержат содержимого ключа, raw
secrets или полного дампа environment/options. Текст сообщений в коде — English.
S-001 controls проверяют observable reason/route; E1 integration проверяет реальную
запись в логах и её связь с request/project/operation, включая unknown/refusal cases.

### Уточнение рабочего процесса владельца

Владелец уточнил: бинарные сборки подписываются на конвейере, обычные разработчики
не имеют доступа к ключу компании. Допуск определяется effective configuration
конкретного ordinary load, а не фактом подписания релизов организации. Signing,
включённый только для CI/release, сам по себе не исключает локальный developer profile.
Если developer configuration всё же зависит от ключа/signing identity, применяется
ordinary route; H-002 не устраняет реальные ошибки отсутствующего ключа и не меняет
build settings. Доступ к ключу компании не требуется для запуска MVP spike;
negative controls могут использовать отдельные тестовые данные.

### Приёмка и принятый риск

S-001 проверяет хотя бы один неизменённый real positive control с metadata/project
references и заранее выбранными observable results в этом contract. Дополнительно
проверяются signing-dependent и unknown cases с ordinary fallback/refusal до
execution/side effects, а также исключённый emit. XML controls H-001 сохраняются.
Negative-only результат не закрывает positive gate; на дату решения 2026-10-07 S-001/S-002 **not-run**.
Текущий статус исполнения — в [spikes](spikes.md).

Владелец предпочёл постепенный полезный MVP: signing-dependent проекты могут не
получить ускорение, но сохраняют ordinary route. Основной риск — ошибочный допуск
зависимого случая, поэтому неизвестность вызывает fallback, а профиль расширяется
только после evidence. H-002 не разрешает production activation и не принимает E0/E1.

Применение и docs-only validation: [H-002 report](h-002-report.md).
