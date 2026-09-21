# 9 — `AnalyzerShadowLoader` (не ship)

Разбор: [../README.md](../README.md#51-analyzershadowloader-гэп-9--почему-не-в-плане-1-4-7).

Не переносить. Тесты форка — только path classification. Нет доказательства
«генератор отработал + исходная DLL перезаписывается», нет private
dependencies, первая версия сидит в default ALC до рестарта MCP.

Если симптом (centralized `bin/` + lock) подтвердится на решении владельца —
новая эпоха существующего overlay/provenance, с integration-тестом. Не второй
shadow рядом с `shadowCopyInSolutionAnalyzers`.
