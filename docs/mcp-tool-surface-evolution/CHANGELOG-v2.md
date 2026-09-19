# Changelog — proposal v1 to proposal v2

Все изменения ниже обязательны по арбитражу. Исходные proposal/review/response/
arbitration файлы не изменены (лежат в [_archive/](_archive/README.md)).

## Material changes

1. **Stage 0 SDK.** Ссылка на отсутствующий `global.json` снята. Windows
   workflow явно ставит .NET SDK семейства `10.0.x`; приёмка — чистый
   runner, лог `dotnet --info` / `--version`, оба test jobs. Создание
   pin-файла не входит в стадию. Проверка revert `b9f2e02` сохранена и не
   выводится из факта отката. [E0-01]
2. **Идентичность без `SymbolKey`.** Предписание `Microsoft.CodeAnalysis.SymbolKey`
   и запрет server-side handle сняты. `symbolId` — непрозрачная строка на
   публичных API Roslyn 5.9.0; клиент формат не разбирает; internal API /
   reflection / adapter в production запрещены. [E2-01]
3. **Session store.** Сервис хранит load-session generation, `ProjectId`,
   `DocumentId`, якорь декларации, все документы multi-decl символа и
   fingerprint полного текста каждого документа объявления. Новый процесс,
   `reset_workspace`, фактический новый load, смена TFM, исчезновение
   project/document инвалидируют ID. Cached load той же session — нет.
   Переносимость между процессами не обещается. [E2-01, E2-03]
4. **Fail-closed после правки документа объявления.** Любое изменение
   полного текста документа объявления даёт `stale-id` (включая local
   functions и замену декларации тем же текстом при иных правках файла).
   Изменение другого документа ID не инвалидирует. Checksum декларации не
   считается доказательством идентичности. [E2-04]
5. **Единый resolver.** Identity validation до preview и до apply; write
   freshness gate её не заменяет. После провала исторической проверки не
   выбирать ближайший/первый/одноимённый символ. [E2-01, E2-04]
6. **Публичные формы вызовов.** Совместимость = прежние валидные вызовы
   остаются валидными. Addressing-поля пяти старых tools optional в JSON
   Schema ради ID-only. Режимы XOR; смесь → `conflicting-selectors`, пусто
   → `missing-selector`. `newName` required. Location-only только у
   `find_usages` и `get_symbol_info`. ID крепится к конкретному кандидату.
   [E2-02]
7. **Project context и linked-файлы.** Location при нескольких memberships
   возвращает различимые candidates и требует выбора; first-hit
   `FindDocumentAsync` не resolver точного режима. Legacy path-only
   сохраняет прежнюю семантику с явной оговоркой точности. [E2-03]
8. **Запись общего пути.** До ID/location-based записи — preflight
   согласованного итогового текста всех memberships нормализованного
   физического пути, иначе `shared-path-conflict` без записи. Писать путь
   один раз. `scope="project"` не изолирует физический файл. [NEW-ARB-001]
9. **Индекс серии.** Stage 2 передаётся в реализацию только по этому
   канону. Stage 1 без изменений. Stage 3 docs-only и не наследует
   `SymbolKey` / portable ID. [индекс серии]
10. **R-§6 mapping.** Норма — session-scoped opaque handle, не
    `SymbolKey`. [E2-01]

## Preserved unresolved gates

- Точный pin SDK репозитория и политика patch. [E0-01 граница] → U-ARB-01.
- Переносимый или durable после правок `symbolId`. [E2-01/E2-04] → U-ARB-02.
- Миграция всех legacy filePath-tools на project-aware выбор. [E2-03]
  → U-ARB-03.

## Closed rejected recommendations

Отклонённых findings нет. Закрытые более сильные варианты ответа автора:

- Обязательный `global.json` в Stage 0. [E0-01]
- Internal `SymbolKey` adapter. [E2-01]
- Location-only на все пять старых tools. [E2-02]
- Durable ID для именованных деклараций по checksum vs fail-closed только
  для locals. [E2-04]

## Migration and runtime impact

Код, public tools, defaults, product docs и версии не изменены. Это
документальная ревизия. Реализация Stage 0/1/2 по-прежнему только по
явному запросу.
