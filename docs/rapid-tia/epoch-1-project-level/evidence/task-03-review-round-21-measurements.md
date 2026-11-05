# task-03 — R21-001 measurements

Дата: 2026-11-05. Before production — 1622e41; исходная comparison base — 1b617c4. [Полные компактные rows/counters](task-03-r21-measurements.json). Windows SDK 10.0.402; Ubuntu WSL SDK 10.0.114, inotify 128/524288/16384, limits и global parallelism не менялись.

Один и тот же real-MSBuild harness: 40 подкаталогов; node_modules/x — 20000 файлов при 50 и 200 C# sources; obj/out — 5000 файлов при 2000 sources. Источники Known, исходный graph свежий, initial package journal пуст. Directory.Delete, не rm/npm. Callback AfterDiskWatcherChangeForTests считает Deleted только внутри payload; fence из того же recursive watcher подтверждает очередь после удаления, при потерянном fence повторяется до receipt. Нет искусственного slowdown, профилировщика или per-event логов.

deleteMs — время синхронного удаления. callbackTailMs — от возврата Directory.Delete до последнего обработанного Deleted, минимум0. Для интерпретации важны оба значения и handledDeleted: overflow обрезает работу, поэтому короткий tail потерявшего события прогона не доказывает throughput. Single-run timings не являются универсальной гарантией или статистической частотой.

## Windows

- node_modules, 50 sources: before delete1463.25ms + tail65.90ms, handled8982/20041, overflow; after1988.70ms +0.0193ms, handled20041/20041, no gap/journal0.
- node_modules, 200 sources: before1485.20ms +1.0657ms, handled7168/20041, overflow; after2008.26ms +0.026ms, handled20041/20041, no gap/journal0.
- obj, 2000 sources: before375.39ms +425.36ms, handled459/5041, overflow; after371.13ms +26.49ms, handled2103/5041, overflow. Original 1b617c4 also overflowed:395.81ms +104.08ms, handled1052/5041. Это подтверждённая baseline limitation, не новая no-gap гарантия для Windows obj.

Before actual3/0/3 содержит2 genuine R21 node failures и избыточный obj no-gap assertion. After first measurement2/1 сохранила тот же inherited obj overflow. После objective comparison1b harness исправлен: obj — measurement, node_modules — строгие no-gap/journal0 guards. Existing deterministic Deleted obj/bin/artifacts suppression остаётся строгой. Production Changed/notice behavior ради obj не менялось. Full final focused611/611 выполнен после этой test-only scope correction.

## Linux WSL

Native overflow до fix здесь не воспроизвёлся: before3/3PASS и baseline3/3PASS, after3/3PASS. Это не называется native Linux RED; deterministic Linux probe дал7/0/7 доfix. KVM review21 evidence остаётся отдельным наблюдением.

- node200: 1b delete262.59ms + tail44.78ms; before269.83ms +980.10ms; after825.67ms +0.0009ms.
- node50: 1b429.83ms +0.0111ms; before270.14ms +544.87ms; after864.27ms +0ms.
- obj2000: 1b88.63ms +1512.84ms; before88.26ms +2529.84ms; after80.26ms +1006.89ms.

Все Linux rows обработали20041 либо5041 Deleted, безgap. Node journal0. Delete-time variance видна, поэтому ускорение total не обещается: deterministic probe доказывает отсутствие map-size scans, измерения показывают фактическую задержку этой среды.

## Воспроизведение и артефакты

Новый BaselineDeletedPathCostTests содержит workload и runtime counters. Windows build/test выполнялись Roslyn MCP; raw TRX — artifacts/rapid-tia-r21-measure/windows-before-final, windows-after, windows-baseline. Linux runner — artifacts/rapid-tia-r21-measure/run-linux.py, phases before/after/baseline/before-det; logs/TRX/source manifests сохранены в соответствующих linux-* папках и summary JSON выше. Baseline Linux clone /home/runner/roslyn-mcp-rapid-tia-r21-baseline-20261105; scratch HEAD1b с prior source overlay, exact hashes в manifests различают его от pristine baseline.

Windows comparison clone сначала под artifacts попал в root Compile glob: pre-test build failed, тесты не стартовали, это не RED. После проверки source/destination внутри workspace он перенесён PowerShell Move-Item -LiteralPath в obj/rapid-tia-r21-windows-baseline-repo; production/project settings не менялись. Последующая сборка и focused успешны. Raw measurements до переноса сохранены.

Known non-C# external root lifetime exception из review21 не исправлялась. Файловое содержимое и прочие события наблюдаются по прежним правилам; новая работа только индексирует статические факты карты.
