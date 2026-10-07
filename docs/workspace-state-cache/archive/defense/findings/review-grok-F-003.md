# review-grok/F-003 — Новый ancestor analyzer config не входит в правило invalidation

Дата: **2026-10-07**. Source: [review-grok/F-003](../../review-grok/findings/F-003.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT

## Finding summary

Новые ancestor .editorconfig/.globalconfig не входят в explicit candidate list; индекс известных configs не обнаруживает их появление.

## Relevant requirements / constraints

- Offline diff должен обнаруживать новые/удалённые значимые inputs, не только изменение известных documents.
- Обход ограничен, поэтому discovery regions/ancestor candidates обязаны иметь конкретные имена и границы.
- Новый config/membership в E1/E2 даёт ordinary fallback; полное синхронное перечитывание всей монорепы не требуется.

## Evidence checked

- [Индекс входов: named walk-up list и generic config region](../../README.md)
- [Приёмка: новый Directory.Build.props](../../epoch-2-content-refresh/spec.md)
- [new ancestor file fallback](../../epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md)
- [CreateRegions/WalkUpFileNames/AnalyzerConfigs coverage limitation](../../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
- [Microsoft Learn: EditorConfig и Global AnalyzerConfig](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files), прочитано 2026-10-07.

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю. Generic «config inputs» не даёт исполнителю explicit probe для отсутствующего файла на ancestor path. Existing builder именует только четыре walk-up candidates и отдельно признаёт неизвестность discovery новых editorconfig вне evaluated Documents. Source directory Merkle не закрывает ancestor file вне этой region.

Документация Microsoft различает folder/file EditorConfig и project Global AnalyzerConfig; именованные .globalconfig могут нести implicit applicability, а произвольно названные configs включаются явно. Следовательно, проверка known-absent candidates и явных GlobalAnalyzerConfigFiles/regions должна отражать actual discovery выбранного SDK. Linked source за пределами project directory также требует своей применимой ancestor цепочки.

Не требуется сканировать каждый ancestor каталог целиком. Ограниченные probes по значимым candidate именам и explicit config paths достаточно описать в profile. До принятого source/discovery rule неопределённый case даёт miss; отсутствие watcher событии во время offline не имеет значения.

## Position rationale

ACCEPT: explicit discovery contract потерян при переходе от списка known config documents к offline membership validation.

## Proposed response

Suggested change: перечислить .editorconfig/.globalconfig absent/present probes и применимые ancestor/linked roots с discovery boundary; root=true/explicit config includes учитывать по выбранному SDK profile. Согласовать E1 manifest/probe и E2 mutation task. Добавить create/delete ancestor cases уже до первого reusable disk hit. Произвольное расширение .config allowlist не заменяет role/discovery evidence.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-008 — связь для рассмотрения, не объединённый dispute.
- review-grok/F-004 — связь для рассмотрения, не объединённый dispute.
