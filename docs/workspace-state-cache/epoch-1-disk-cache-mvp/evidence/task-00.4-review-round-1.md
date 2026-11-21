# E1/task-00.4 — независимое ревью, round 1

Дата: **2026-10-09**, Europe/Moscow. Рецензент: отдельный Codex reviewer
subagent, унаследованная модель сессии; точные runtime model ID/reasoning
**not confirmed**. Исполнитель — другой Codex subagent. Коммитов нет.

Verdict: **ACCEPT** для bounded ordinary research с отрицательным результатом
**no-go**. Открытых must-fix findings: **0**; исправлений после этого ревью: **0**.
Production optimization, выбор ordinary маршрута, отсрочка disk restore,
manifest readiness, приёмка E1 и activation этим verdict не принимаются.

Проверенный [report](task-00.4-ordinary-alternative.md), SHA256:
`B311C7CBBBD3319491A952EB71D18757521912659C1FF6A620493465941A06E2`.
Scope: task-00.4 целиком, E1 spec/index, series README, execution/benchmark,
task-00, accepted E0/task-07 report/review/raw summaries, task-11 S-002 validation;
owned profiling driver, analysis command/result, final raw attempt и предыдущие
setup/attach/load/rundown failures. Current pipeline claims сверены с
SolutionManager, AnalyzerProvenanceCaptureService, Program, NavigationTools и
SymbolDeclarationResolver; E2/E3 dependencies сверены с индексами.
Посторонние dirty C#/tooling файлы не входят в reviewed implementation scope.
C# правок и C# style review не было.

Проверка evidence:

- E0 ordinary и isolated restore сохранены на двух отдельных compare keys;
  n=5, excluded warmup, medians и failure accounting соответствуют summaries.
  Isolated useful не назван production hit или эффектом ordinary оптимизации;
  capture/startup и new-process/warm-files/RAM режимы различены.
- Final diagnostic PID 79380 дал ожидаемый SamplePlugin type/file/line.
  Phase timestamps независимо дают request-only load **1535.6 ms**, semantic
  **981.4 ms**; useful **2517.0 ms**. Raw load/useful раскрывают **79.8 ms** attach
  wait. Sampling overhead неизвестен, n=1 не допускается в новую median.
  Source HEAD и actual published product commit/version различены.
- Два setup not-runs, failed attach до load, sandbox load failure, semantic
  success с broken rundown и final usable trace сохранены раздельно. Запрошено
  три actual loads, два semantic successes; query misses/request timeouts — 0.
  Final speedscope содержит **3058 frames / 22 profiles**; matchedFrames из
  analysis присутствуют в raw. Concurrent waits/post-response idle и отсутствие
  BuildHost child trace исключают доказательство critical-path hotspot.
- Final semantic raw содержит membership warning о появлении/удалении `.cs`
  вне snapshot. Report не использует этот diagnostic как доказательство full
  graph freshness или как comparable ordinary baseline: source identity и
  before/after hashes не объявлены manifest/P-004 proof. Warning дополнительно
  ограничивает возможную интерпретацию этих n=1 timings.
- No-go допускается task contract при неподтверждённом hotspot. Предложен один
  bounded replay measurement с seam, counts/bytes/spans, n>=5, failure accounting
  и stop condition; числовой эффект будущей оптимизации не выдуман.
  RAM matcher/globals, sticky overlay/admission, diagnostics, cancellation,
  resource ownership и write boundaries входят в будущий regression scope.
  Host seam не объявлен ускорением и не активирован.
- E0 artifacts и ordinary/live RAM/watch/write capabilities сохраняются;
  E1 manifest и E2/E3 restart/selective outcomes остаются недоступны по своим
  dependencies. Downstream status consequences описаны условно, до отдельного
  owner decision; task-00 blocked не снят.

Final validation принадлежит координатору. Сохранённый
[validation-tools.json](task-00.4/validation-tools.json) содержит успешный Release
solution build и полный main `Category!=AnalyzerLifecycle`: **1363 total /
1363 passed / 0 failed**. SourceStructure входит в main; lifecycle code/config
не менялись, AnalyzerLifecycle not-run / not required. Полные тесты рецензент
повторно не запускал. Pending task/index/final acceptance bookkeeping не считается
implementation finding этого раунда и должно быть завершено координатором.
