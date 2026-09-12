# S7 — принять исправления v3 по runtime evidence

Статус: **не выполнено**. Зависимость: [S6](s6-contract-alignment.md).
Результат шага: итоговый проверяемый вердикт по R1–R4 и архитектурным гарантиям v3.

## Работа

Провести итоговый прогон на актуальном commit после всех исправлений.
Зафиксировать версию, OS, разрядность host, SDK/Roslyn, команды MCP и
длительности. Build должен соответствовать проверяемому коду; результаты
старой DLL не принимаются. Для влияющих на CLR тестов использовать отдельные
host-процессы; reset workspace не заменяет изоляцию процесса.

| Проверка | Обязательное доказательство |
| --- | --- |
| R1 / freshness | Stale same-session и stale-after-reload отклонены до writes; свежие тексты и project bytes сохранены |
| R2 / устойчивый fail-closed | После prepare failure/cancellation и последующих edit/flush/reconciliation real analyzer не появляется |
| R3 / capture gate | Missing/corrupt/incomplete capture не даёт semantic snapshot; cached calls не снимают запрет |
| Частичный prepare | Несколько references не создают смешанный shadow/confirmed-real snapshot; сводка честно отражает applied/stale/blocked |
| Успешный opt-in | Existing-correct и redirected missing fixtures исполняют exact marker из shadow path |
| Anti-lock | Real path отсутствует в process assemblies; forced rebuild успешен и меняет hash real DLL |
| Запись | Exact inverse сохраняет `.csproj`; неизвестный analyzer diff отклоняется; частичный исход не выдаётся за полный успех |
| Переходы сессии | Cached true/false/omitted, reset, graph reopen и смена load key сохраняют оговорённый допуск |
| CLR/dependencies | Restart-required и main-only остаются действующими; private helper не разрешается как fallback |
| Документация | R4 закрыт; inaccessible явно остаётся вне принятой матрицы |

Использовать regression tests S1 и дополнительные проверки S2–S5 вместе с
релевантными существующими `WorkspaceWriteBoundaryTests`,
`AnalyzerShadowGenerationPublisherTests`, `AnalyzerProvenanceBindingTests`,
`AnalyzerLoaderContractTests`, `Epoch1WritePathTests`, `Epoch3LoaderContractTests`,
`Epoch4WorkspaceWriteTests`, `F09ProductionCaptureTests`,
`Epoch5S4AcceptanceTests`, `UArb04LoadBoundaryEvidenceTests` и semantic inventory.
Выбор фильтров и непокрытые сценарии перечислить явно. Не запускать исторические
красные ожидания как неизвестную регрессию: их текущий контракт нужно учитывать.

Проверить MCP catalog/schema по существующим guard tests. Если изменены
описания tools, обновлённые ожидания должны следовать согласованному help,
а не служить способом скрыть изменение публичных параметров.

## Приёмка

- R1–R3 имеют сохранённые зелёные regression-тесты на production workflow;
  failure injection не подменяет сам manager/test oracle реализацией-заглушкой.
- В таблице выше для каждой строки есть фактический результат и ссылка на
  тест/evidence. Skip/timeout не считается pass.
- Нет новых блокирующих нарушений публикации, persistence или identity gate.
  При обнаружении дефекта вердикт остаётся «не принято» до исправления и
  повторной проверки затронутого сценария.
- Указаны commit и версия реализации. Если выпуск выполняется отдельно,
  различаются «проверено в исходниках» и «проверен опубликованный MCP binary»;
  публикация не подразумевается одним прохождением тестов.
- README v3 получает итоговый статус только по результатам этого шага.
  Открытый inaccessible не скрывается и не объявляется автоматически решённым.

## Результат

Не выполнено.
