# E5-S3 — диагностика provenance matcher

Дата: 2026-09-12. Статус: **реализовано в v1.3.11; независимая приёмка
[принята](epoch-5-s3-acceptance.md)**.
Область: внутренняя диагностика результата и компактная текстовая сводка.
Публичная структурированная MCP schema, matcher, inaccessible policy и
U-ARB-05 не изменялись.

## Реализация

- Каждый рассмотренный analyzer reference получает стабильный reason code:
  `reference_rewritten`, `source_output_missing`, `ambiguous_assembly_name`,
  `proven_foreign_path`, `provenance_unconfirmed`, `access_failure` или
  `preparation_failure`.
- Original path и выбранный source output имеют независимые состояния:
  `NotProvided`, `Exists`, `Missing`, `AccessFailure` или `Invalid`.
- Результат отдельно хранит original path, выбранный source project,
  selected-source path, основание выбора и generation.
- Filename/assembly-name используется только для ограничения диагностических
  кандидатов при отсутствии binding; это не подтверждает provenance и не
  разрешает rewrite.
- `MissingSourceProject` из complete snapshot с
  `MSBuildSourceProjectFile` даёт `proven_foreign_path`. Простое отсутствие
  binding остаётся `provenance_unconfirmed`.
- Ошибка доступа к source отделена от отсутствующего output и от прочего сбоя
  публикации. Известная конкретная причина не заменяется generic unconfirmed.
- Сводка `load_workspace` остаётся текстовой и считает rewritten/skipped по
  фактическому `Applied`, включая ambiguous/unconfirmed результаты. Подробности
  с reason code и обоими path states пишутся в server log.

## Проверка

- `AnalyzerReferenceShadowCopierTests`: rewrite, missing source, ambiguous,
  proven foreign, unconfirmed, preparation failure и access failure.
- `AnalyzerProvenanceBindingTests`: exact-output/TFM binding без регрессии.
- `F09ProductionCaptureTests`: applied rewrite считается отдельно от
  диагностических skipped references; failed capture остаётся fail-closed.
- Debug build выполняется с `--no-incremental`.
