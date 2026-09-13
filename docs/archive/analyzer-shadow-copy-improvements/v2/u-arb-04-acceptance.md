# Приёмка U-ARB-04 — evidence load boundary

Дата: 2026-09-12. Вердикт: **evidence-файл принимается. Leftover U-ARB-04-1
независимо принят: публичные сводки называют trigger published
`GetCurrentSolution()` без overlay; test-only raw reader оставлен только
после overlay. Выбор atomic load/prepare boundary этим leftover не
принимался.**
Норматив: [UNRESOLVED-v2.md](UNRESOLVED-v2.md) U-ARB-04.
Самоотчёт: [u-arb-04-load-boundary-evidence.md](u-arb-04-load-boundary-evidence.md).
Inaccessible не открывался.

Независимый прогон (локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`, SDK 10.0.204):

- `dotnet build` Debug `--no-incremental`: success
- `UArb04LoadBoundaryEvidenceTests`: **4 passed / 0 failed**
- `Epoch1WritePathTests`: **6 passed / 0 failed**
- `Epoch1SemanticInventoryTests`: **3 passed / 0 failed**

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| Load/prepare lazy | — | Physical load ± prepare без semantic: Generator не в process assemblies; forced rebuild меняет hash | **закрыт** |
| No-overlay published semantic | — | `GetCurrentSolution()` + compilation → exact real `Generator.dll`, затем `MSB3021`; cached enable=true не активирует overlay | **закрыт** |
| Overlay + write paths | — | Semantic/write грузят shadow; real path отсутствует; rebuild меняет hash; `.csproj` byte-identical | **закрыт** |
| Inventory | — | Нет `GetWorkspaceCurrentSolution()` + compilation в `Tools/`/`Services/` | **закрыт** |
| New boundary | — | Production code / schema не менялись | **закрыт** |
| U-ARB-04-1 | Medium | README / ARCHITECTURE / epoch-4-acceptance называли lock «test-only raw reader» | **принят** (независимая сверка сводок) |

Evidence-файл сам честен: published snapshot без overlay несёт raw references;
обычная production semantic operation может заблокировать existing-correct
output; это opt-in граница, не обход активного overlay; concurrent dispatch
между двумя lock-захватами одного `load_workspace(..., true)` не измерен;
write boundary не есть load isolation. Decision gate остаётся открытым.

Версия **v1.3.13** (bump не нужен: test-only host + docs, публичная tool
behavior не менялась).

---

ID: U-ARB-04-1
Severity: Medium
Depends: —

Target:
README.md «Measured anti-lock scope»
docs/ARCHITECTURE.md U-ARB-04 bullet
docs/analyzer-shadow-copy-improvements/v2/epoch-4-acceptance.md
docs/analyzer-shadow-copy-improvements/v2/epoch-1-semantic-entry-points.md

Claim:
Тест `Existing_correct_path_published_semantic_before_enable_*` вызывает
default oracle = `GetCurrentSolution()`, не `oracleSource: workspace`.
`oracleSource: workspace` используется только **после** уже выполненной
overlay semantic и **не** грузит отдельную real assembly.

До исправления сводки писали «test-only raw `workspace.CurrentSolution` /
явный raw reader; production raw reader не найден». Это противоречило
evidence-файлу и тесту: lock — production-equivalent published compilation при
выключенном overlay.
Владелец, читая только README, решит, что утечки в production нет.

Suggested change:
В сводках повторить формулировку evidence: published no-overlay
`GetCurrentSolution()` / обычная semantic operation. Test-only raw reader
оставить только для строки «после overlay → reuse shadow». Не чинить
matcher, не выбирать boundary этим leftover.

Resolution (независимая сверка, не самоотчёт implementer):
README EN/RU, `ARCHITECTURE.md`, `epoch-4-acceptance.md` и
`epoch-1-semantic-entry-points.md` разделяют:
published no-overlay `GetCurrentSolution()` = измеренный lock / production
opt-in boundary; `oracleSource: workspace` только после overlay = reuse
shadow, без отдельной real assembly. Старой формулировки «test-only raw
до enable / production raw reader не найден» в этих файлах больше нет.

---

## Что дальше

U-ARB-04-1 закрыт. Решение atomic load/prepare позднее
[принято отдельно](u-arb-04-decision-acceptance.md), а реализация
[принята](u-arb-04-implementation-acceptance.md) в v1.3.14.
Inaccessible не открывать. E5-S3-1, A4-09…A4-12, A6-13, F-06,
U-ARB-05-1 не чинить без отдельного запроса.
