# Response: разбор review `mcp-build-progress-notifications`

Каталог ответов на [`../review/`](../review/README.md). Файлы `review/`,
спеки S1–S4 и сам план не менялись.

Опора (в этом порядке): исходные требования S1–S3 и README каталога
(«Что это не делает», «Фиксированные решения»); shipped-код v1.3.26;
runtime `get_mcp_server_info` = **RoslynMcpServer v1.3.26.0**, 63 tools, `full`.

Сводка: [`summary.md`](summary.md).

| ID | Sev | Verdict | Spec |
| --- | --- | --- | --- |
| R-01 | High | ACCEPT | README.md (план) |
| G-01 | Medium | ACCEPT | README.md (план) |
| S1-01 | Medium | ACCEPT | s1-cli-runner-progress-seam.md |
| S1-02 | Medium | ACCEPT | s1-cli-runner-progress-seam.md |
| S2-01 | High | ACCEPT | s2-build-probe-progress.md |
| S2-02 | High | ACCEPT | s2-build-probe-progress.md |
| S2-03 | Medium | ACCEPT | s2-build-probe-progress.md |

Карточки S1/S2 — в [`s1-cli-runner-progress-seam.md`](s1-cli-runner-progress-seam.md)
и [`s2-build-probe-progress.md`](s2-build-probe-progress.md). В `s3-docs-and-claims.md`
и `s4-test-and-run.md` карточек не было, отвечать не на что.

---

ID: R-01
Verdict: ACCEPT

Критика верна. Граница шага отправляла stopwatch всего probe в поле,
задокументированное как «время с начала шага», поэтому на живом
эскалационном probe часы уезжали назад.

Требования:
README каталога / «Фиксированные решения»: «Текст progress: шаг, elapsed,
last exit если есть»; S2 / «Работа»: «Репорт на смене шага»; shipped-инвариант
`Services/CliProgress.cs`: `Elapsed` — «wall-clock time since the step started».

Что менять:
Не менять семантику поля, а убрать из него probe-часы:
`Services/DotNetBuildProbe.cs` / `RunStepAsync` → `ReportStepStarted` больше не
принимает elapsed и шлёт `TimeSpan.Zero`; `Services/CliProgress.cs` /
`Describe()` печатает `«<шаг>: starting»` при нуле и уточняет XML-doc
(«never since the probe started»). Wall-clock всего probe нигде не репортится —
второе поле под него не вводим, требование такого не просило.

Последствия:
Observability: метка «still running (180s)» на старте restore исчезает.
Тесты: `DotNetBuildProbeTests` (границы без elapsed, `starting`),
`BuildProgressIntegrationTests.Report_emits_strictly_increasing_seconds_…`.
Публичный API тулов, каталог и exit сборки не затронуты.

Шире finding:
нет.

---

ID: G-01
Verdict: ACCEPT

Критика верна: `Progress = 1, 2, 3…` — единственное числовое поле канала, и
хост, рисующий бар, читает его как процент готовности.

Требования:
SDK XML (`ProgressNotificationValue.Progress`): типично 0–100 как процент
либо счётчик вместе с `Total`, значение должно расти; README каталога:
«Не шлёт полный лог MSBuild в progress (только короткие счётчики шага)» —
то есть числа не обязаны быть процентом, но и вводить в заблуждение не должны.

Что менять:
`Tools/McpToolProgressReporter.cs` / `Report`: числовое = целые секунды
elapsed, приведённые к строгому росту running-max (per-step часы сбрасываются,
значение — нет), `Total` не отправляется. Явно записано в коде, README
(v1.3.26), `docs/ARCHITECTURE.md` и handoff'е: числовое поле — не семантика
готовности, хостам рендерить `Message`.

Последствия:
Observability: бар без `Total` не может показать «почти 100 %». Тесты:
`Report_emits_strictly_increasing_seconds_…` (граница шага не роняет число).
Публичный контракт и каталог не менялись.

Шире finding:
NEW-D-02 (см. `summary.md`).
