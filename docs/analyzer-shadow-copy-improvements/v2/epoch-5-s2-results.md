# E5-S2 — rollout provenance matcher

Дата: 2026-09-12. Статус: **реализовано в v1.3.10; независимая приёмка
[принята](epoch-5-s2-acceptance.md)**.
Область: только нормативная таблица E5-S2; reason codes/schema E5-S3,
inaccessible и U-ARB-05 не изменялись.

## Реализация

- Подготовка shadow generation принимает только `Confirmed` binding из
  `Complete` F-09 snapshot той же `loadSessionId`.
- Ключ — exact `(consumer ProjectId, original AnalyzerReference.FullPath)`;
  source выбирается только при одном distinct loaded `SourceProjectId`.
- Filename/`AssemblyName`, first-project и incomplete-capture fallback удалены
  из matcher и reapply mapping.
- Source binding требует exact resolved output даже при одном loaded candidate.
  Противоречие `NearestTargetFramework` и effective nested
  `TargetFramework` оставляет binding unconfirmed.
- Explicit same-name external и unconfirmed missing ссылки сохраняются.
  Отсутствующий resolved output confirmed source не подготавливается.
- Epoch-3 strip/block gate использует тот же confirmed provenance set и не
  удаляет same-name foreign references.

## Проверка

- `AnalyzerReferenceShadowCopierTests`: confirmed-only, no-provenance,
  foreign preservation, ambiguous source и source-output missing.
- `AnalyzerProvenanceBindingTests`: single-candidate exact-output gate и
  effective-TFM conflict.
- `F09ProductionCaptureTests`: production snapshot, fail-closed capture и два
  реально загруженных inner TFM с разными prepared outputs; exact `V1`
  проверен для обоих consumer в отдельных свежих host-процессах, чтобы
  одинаковая CLR identity двух TFM не смешивала provenance с epoch-3 gate.
- Lifecycle markers:
  - redirected missing analyzer path → exact `V1`;
  - existing same-name external без analyzer edge → unchanged path и exact
    `FOREIGN`;
  - missing same-name external остаётся missing и не подменяется source output.

Build Debug `--no-incremental` успешен. Новая structured MCP schema и E5-S3
reason codes не вводились.
