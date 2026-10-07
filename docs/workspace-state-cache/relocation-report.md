# Перенос истории и публикация канона в корне темы

Дата: **2026-10-07**. Scope: docs relocation по запросу владельца; requirements,
implementation gates и решения не переоцениваются. Статус: **relocated / docs prepared**.
Финальный author consistency check — [validation](validation.md) и
[machine result](validation.json); independent recheck/runtime implementation не выполнены.

## Расположение и сохранность

- **129** исходных файлов specification/review/defense/arbitration перенесены
  в [archive](archive/index.md). Исходные epoch/task IDs и исторические outcomes сохранены.
- Все **59** файлов подготовленной specification v2 подняты из `spec-v2/` в корень
  `docs/workspace-state-cache/`: четыре эпохи, **35 tasks (9 / 12 / 7 / 7)**,
  common requirements/benchmark/execution, H-001/U-001/spikes и revision evidence.
- Перед move исходные 129 файлов сверены с прежним input manifest; после native move
  все **188** файлов побайтово сверены с непосредственно предшествующим снимком.
  Данные при перемещении не потеряны и не смешаны по одинаковым task names.
- Затем rebased Markdown links в **126** файлах, включая docs index. References
  истории ведут к archived inputs; внутренние ссылки канона — к актуальным tasks.
  Generic backlinks backlog к корню темы продолжают вести к актуальному канону.
- В archive/README добавлена archival notice; архивные JSON/source snapshots и
  historical scripts не переписаны. [Path/hash map](relocation-manifest.json)
  содержит before/after-link-rebase SHA-256 каждого перенесённого файла.

## Учёт и границы

Root README/epoch summaries, docs index, commands root validator и revision reports
согласованы с новым расположением. Original input-manifest хранит pre-revision
paths/hashes; relocation-manifest делает их проверяемыми после изменения ссылок,
не подменяя исторические snapshots. Archive index/report — новые navigation records.

На момент relocation H-001 исключал external binary XML documentation из guarantees MVP,
strong-name часть U-001 была unresolved (впоследствии решена [H-002](h-002-report.md)); S-001/S-002 not-run, optional E3 прежний. Task implementation
planned, independent review/fix counters не увеличены. Archive move не означает
принятия реализации, permission её запуска, изменения default или public activation.

Изменения ограничены `docs/workspace-state-cache/` и routing строкой `docs/README.md`.
User C#/tests/config/tooling files не редактировались и не включаются в docs commit.
Build/main CI/SourceStructure/AnalyzerLifecycle not-run: docs-only operation.

После author validation документация коммитится отдельным docs commit по прямому
запросу владельца. Commit scope проверяется по staged inventory; фактический hash
сообщается пользователю и не записывается внутрь собственного commit как prediction.
