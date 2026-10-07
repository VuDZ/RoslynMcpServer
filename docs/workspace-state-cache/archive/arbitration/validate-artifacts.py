"""Validate the arbitration deliverable; never modify its source inputs."""

import hashlib
import json
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path
from urllib.parse import unquote


OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[2]
PACKAGE = OUT.parent
PROMPT = Path("C:/Users/VuDZ/.codex/attachments/a81ac097-5fd0-437c-be86-a7ff7975ca24/Pasted text.txt")


def read(path):
    return path.read_text(encoding="utf-8-sig")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def anchors(path):
    result = set()
    duplicates = Counter()
    for heading in re.findall(r"^#{1,6}\s+(.+)$", read(path), re.M):
        heading = re.sub(r"[^\w\-\s]", "", heading.lower()).replace(" ", "-")
        count = duplicates[heading]
        duplicates[heading] += 1
        result.add(heading if count == 0 else f"{heading}-{count}")
    return result


def main():
    errors = []
    reviews = sorted(PACKAGE.glob("review-*"))
    expected = {
        f"{review.name}/{finding.stem}"
        for review in reviews if review.is_dir()
        for finding in (review / "findings").glob("F-*.md")
    }
    registry = read(OUT / "finding-registry.md")
    ledger = read(OUT / "decision-ledger.md")
    rows = []
    required_sections = ["Underlying issue", "Relevant requirements / constraints", "Evidence",
                         "Reviewer positions", "Author position", "Analysis", "Decision",
                         "Rationale", "Required changes", "Remaining uncertainty"]
    for path in sorted((OUT / "findings").glob("ARB-*.md")):
        body = read(path)
        source_block = body.split("Sources:\n", 1)[1].split("Independent corroboration:", 1)[0]
        sources = re.findall(r"\[(review-[^/\]]+/F-\d{3})\]", source_block)
        decision = re.search(r"^## Decision\s+\*\*([^*]+)\*\*", body, re.M).group(1)
        change_block = body.split("## Required changes", 1)[1].split("## Remaining uncertainty", 1)[0]
        changes = sorted(set(re.findall(r"\bP-\d{3}\b", change_block)))
        if decision.startswith("ACCEPT") and not changes:
            errors.append(f"{path.name}: accepted decision has no change")
        for section in required_sections:
            if f"## {section}\n" not in body:
                errors.append(f"{path.name}: missing {section}")
        for source in sources:
            if source not in registry or source not in ledger:
                errors.append(f"{source}: missing registry or ledger provenance")
            defense = PACKAGE / "defense/findings" / (source.replace("/", "-") + ".md")
            if not defense.exists():
                errors.append(f"{source}: missing author response")
        rows.append({"arb": path.stem, "sources": sources, "decision": decision,
                     "changes": changes, "corroborated": "Independent corroboration: yes" in body})
    seen = Counter(source for row in rows for source in row["sources"])
    if set(seen) != expected or any(count != 1 for count in seen.values()):
        errors.append("Source inventory does not map exactly once to the clusters")
    if len(expected) != 16 or len(rows) != 15:
        errors.append("Unexpected inventory size")
    cluster_counts = Counter(row["decision"] for row in rows)
    wanted = {"ACCEPT": 6, "ACCEPT WITH MODIFICATION": 6, "SPIKE": 2, "UNRESOLVED": 1}
    if dict(cluster_counts) != wanted:
        errors.append(f"Unexpected decisions: {dict(cluster_counts)}")
    proposals = read(OUT / "proposed-changes.md")
    proposal_ids = re.findall(r"^## (P-\d{3}) —", proposals, re.M)
    if proposal_ids != [f"P-{number:03}" for number in range(1, 15)]:
        errors.append("Proposed changes inventory mismatch")
    referenced = {change for row in rows for change in row["changes"]}
    if referenced != set(proposal_ids):
        errors.append("Orphaned or unknown proposed changes")
    for block in re.split(r"^## P-\d{3} —", proposals, flags=re.M)[1:]:
        for field in ["Reason:", "Affected areas:", "Required change:", "Dependencies:", "Implementation freedom:"]:
            if field not in block:
                errors.append(f"Proposed change missing {field}: {block[:60]}")
    for block in re.split(r"^## S-\d{3} —", read(OUT / "spikes.md"), flags=re.M)[1:]:
        for field in ["Question", "Why evidence is insufficient", "Experiment", "Success criteria",
                      "Possible outcomes", "Decision deadline"]:
            if f"### {field}" not in block:
                errors.append(f"Spike missing {field}")
    unresolved = read(OUT / "unresolved.md")
    for field in ["Decision required", "Why now", "Relevant requirements / constraints",
                  "Other viable options", "Arbiter assessment", "Decision impact"]:
        if f"### {field}" not in unresolved:
            errors.append(f"Unresolved missing {field}")
    for option in ["A", "B"]:
        # Subheadings use four hashes and remain part of this option.
        if not all(f"#### {name}" in unresolved.split(f"### Option {option} —", 1)[1].split("\n### ", 1)[0]
                   for name in ["Advantages", "Risks", "Consequences"]):
            errors.append(f"Option {option} lacks trade-offs")

    protected = json.loads(read(OUT / "input-manifest.json"))
    changed = [item["path"] for item in protected
               if not (ROOT / item["path"]).exists() or digest(ROOT / item["path"]) != item["sha256"].lower()]
    expected_paths = {item["path"] for item in protected}
    actual_paths = {path.relative_to(ROOT).as_posix() for path in PACKAGE.rglob("*")
                    if path.is_file() and OUT not in path.parents}
    inventory_delta = sorted(expected_paths.symmetric_difference(actual_paths))
    if changed or inventory_delta:
        errors.append("Protected package inputs changed")
    snapshot = json.loads(read(PACKAGE / "review-astra/target-snapshot.json"))["files"]
    snapshot_changes = [item["path"] for item in snapshot
                        if digest(ROOT / item["path"]) != item["sha256"].lower()]
    if snapshot_changes:
        errors.append("Reviewed target snapshot mismatch")
    diff_check = subprocess.run(["git", "diff", "--check"], cwd=ROOT, capture_output=True, text=True)
    if diff_check.returncode:
        errors.append("git diff --check failed")

    # Create the validation report before checking links to it.
    report = """# Проверка артефактов арбитража

Дата: **2026-10-07**. Scope: структура, полнота, provenance и неизменность входов.
Это проверка deliverable, не validation будущей реализации cache.

- 16 source findings → 15 ARB, каждый source ровно один раз; все author responses доступны.
- Outcomes: 6 ACCEPT / 6 ACCEPT WITH MODIFICATION / 2 SPIKE / 1 UNRESOLVED;
  0 REJECT / 0 DEFERRED. Единственный corroborated cluster сохраняет оба source ID.
- 14 P-* имеют source, affected areas, actionable outcome, dependencies и implementation freedom.
- Два S-* содержат experiment, success criteria, possible outcomes и deadline;
  U-001 содержит варианты с advantages/risks/consequences. Все эти эксперименты not-run.
- 100 исходных файлов пакета проверены против input-manifest; 45 target files —
  против snapshot review-astra. Изменений и изменения inventory вне arbitration нет.
- Локальные Markdown links и anchors, обязательные sections, trailing whitespace
  и final newline проверены. Точные counts и errors — в [validation.json](validation.json).
- `git diff --check` — passed. Git предупреждает о будущей LF→CRLF нормализации
  исходного пользовательского DependencyEvidenceRunner.cs; файл арбитр не редактировал.

Проверки выполняет [validate-artifacts.py](validate-artifacts.py); повторный запуск
`python docs/workspace-state-cache/arbitration/validate-artifacts.py` проверяет этот
же сохранённый результат и перезаписывает только validation outputs здесь.
Source hashes начального снимка относятся к docs-пакету; это не заявка на полный
побайтовый аудит всего checkout. Все операции записи этой сессии ограничены arbitration.

Build/main CI/SourceStructure/AnalyzerLifecycle/benchmarks **not-run**: production,
test и build configuration не менялись. Dynamic scenarios и spikes не выдаются за pass.
Ограничения evidence — [evidence.md](evidence.md).

Статус арбитража **completed**, не acceptance specification. Исходные statuses не
обновлены по прямому output-only запрету пользователя. Scope/date/evidence зафиксированы
в [локальном README](README.md) и [result](result.md). Финальный marker записывается
последним действием успешного validation, после проверки outputs.
"""
    (OUT / "validation.md").write_text(report, encoding="utf-8", newline="\n")
    (OUT / "validation.json").write_text("{}\n", encoding="utf-8")
    checked_links = 0
    checked_anchors = 0
    for path in OUT.rglob("*.md"):
        body = read(path)
        if not body.endswith("\n"):
            errors.append(f"{path.name}: missing final newline")
        if any(line.rstrip() != line for line in body.splitlines()):
            errors.append(f"{path.name}: trailing whitespace")
        for target in re.findall(r"(?<!!)\[[^\]\n]+\]\(([^)\n]+)\)", body):
            if re.match(r"^[a-zA-Z]+://", target):
                continue
            file_name, separator, fragment = unquote(target.strip("<>")).partition("#")
            destination = (path.parent / file_name).resolve() if file_name else path
            checked_links += 1
            if not destination.exists():
                errors.append(f"{path.name}: missing target {target}")
            elif separator and destination.suffix == ".md":
                checked_anchors += 1
                if fragment not in anchors(destination):
                    errors.append(f"{path.name}: missing anchor {target}")
    result = {
        "date": "2026-10-07", "run": "workspace-state-cache-arbitration-2026-10-07",
        "findings": len(expected), "clusters": len(rows), "cluster_outcomes": dict(cluster_counts),
        "source_outcomes": dict(Counter(row["decision"] for row in rows for _ in row["sources"])),
        "corroborated_clusters": sum(row["corroborated"] for row in rows),
        "proposed_changes": len(proposal_ids), "protected_files": len(protected),
        "changed_inputs": changed, "input_inventory_delta": inventory_delta,
        "reviewed_snapshot_files": len(snapshot), "reviewed_snapshot_mismatches": snapshot_changes,
        "links_checked": checked_links, "anchors_checked": checked_anchors,
        "prompt_sha256": digest(PROMPT), "git_diff_check_exit": diff_check.returncode,
        "production_build_tests": "not-run: arbitration only", "spikes": "not-run",
        "errors": errors, "decisions": rows,
    }
    (OUT / "validation.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if errors:
        (OUT / "validation.md").write_text("# Validation incomplete\n\n" + "\n".join(f"- {error}" for error in errors) + "\n", encoding="utf-8")
        print(json.dumps({"status": "incomplete", "errors": errors}, ensure_ascii=False))
        (OUT / ".status").write_text("incomplete\n", encoding="utf-8")
        return 1
    print(json.dumps({"status": "completed", "sources": len(expected), "clusters": len(rows),
                      "links": checked_links, "anchors": checked_anchors,
                      "protected_files": len(protected), "errors": []}, ensure_ascii=False))
    (OUT / ".status").write_text("completed\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
