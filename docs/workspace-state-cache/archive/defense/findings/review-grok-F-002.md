# review-grok/F-002 — Evaluated imports и restore inputs не из чего снять

Дата: **2026-10-07**. Source: [review-grok/F-002](../../review-grok/findings/F-002.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

E1 manifest требует actual import/restore paths, но current input-map и portable provenance snapshot не предоставляют согласованный complete source этих категорий.

## Relevant requirements / constraints

- Actual imports/restore inputs должны входить в manifest поддержанного профиля; одни csproj/walk-up names их не заменяют.
- Не требуется новый общий MSBuild interpreter или обход всей SDK установки.
- Неполное evidence запрещает reusable capture, но source должен позволять хотя бы один реальный supported request.

## Evidence checked

- [task-00-input-manifest-hi.md](../../epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md)
- [Стартовая проверка](../../epoch-1-disk-cache-mvp/spec.md)
- [CreateCategories: imports/restore unknown](../../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
- [ProjectImports=None; ReplayOne/ReadProperties; binlog cleanup](../../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)
- [Microsoft Learn: BinaryLogger parameters](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference?view=visualstudio), прочитано 2026-10-07.
- [Upstream BinaryLogger.cs: Write и CollectImports](https://source.dot.net/Microsoft.Build/Logging/BinaryLogger/BinaryLogger.cs.html), прочитано 2026-10-07; не runtime-version equivalence proof.

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю отсутствующий evidence-source contract: текущий WorkspaceInputMap declares unknown imports/restore, а ReplayOne не строит evaluated-import inventory. Читатель не может принять существующий AnalyzerProvenanceSnapshot за готовый dependency manifest. До cleanup нужны source и правило incomplete→unsupported.

Модифицирую вывод, что ProjectImports=None сам по себе делает import paths недоступными, и что неизбежны второй binlog либо embedding source. Этот параметр управляет сбором project/import файлов. В просмотренном upstream BinaryLogger event Write отделён от CollectImports; отключение file collection не является доказательством отсутствия ProjectImported events. Current ReplayOne сохраняет также весь доступный ProjectStarted Properties dictionary через ReadProperties, а не только несколько fixed properties. Наличие конкретного ProjectAssetsFile в реальном логе всё ещё надо подтвердить, но нельзя утверждать его отсутствие исключительно по форме snapshot.

Upstream code и docs не доказывают полноту installed 18.6.3 raw events. Я не объявляю closure уже доступной. Минимальный first option — проверить извлечение путей/контекстов из существующего ordinary binlog до удаления, сохранив None; иной канал нужен лишь для реально недостающего evidence. SDK version fingerprint — выбранное доверие к toolset в MVP, не checksum каждого imported SDK file.

## Position rationale

ACCEPT WITH MODIFICATION: source contract отсутствует; обязательная смена logger collection mode и абсолютная недоступность paths не следуют из checked evidence.

## Proposed response

Suggested change: task-00 называет категории, source events/properties, binding к instances и incomplete outcome; короткий raw-log control проверяет imports/restore без source embedding. Не менять provenance gate семантику ради manifest. Если существующих events не хватает, вынести выбор дополнительного канала и его стоимость/содержимое на design/owner decision. Не подменять одно source-backed positive evidence общей гарантией полноты.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-astra/F-001 — связь для рассмотрения, не объединённый dispute.
- review-grok/F-004 — связь для рассмотрения, не объединённый dispute.
