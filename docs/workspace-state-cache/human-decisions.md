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

### Оставшееся решение

Strong-name/signing часть U-001 **UNRESOLVED**: разрешения терять provider search
paths или signing/diagnostic/emit behavior владелец этим решением не дал. S-001
остаётся not-run и требует остаточного fidelity решения до positive executable scope.
Точное представление DLL references/aliases/EmbedInteropTypes и ordinary fallback
при изменениях inputs не отменяются.

Применение к файлам и проверка: [change ledger](change-ledger.md#h-001--решение-владельца)
и [revision report](preparation-report.md).
