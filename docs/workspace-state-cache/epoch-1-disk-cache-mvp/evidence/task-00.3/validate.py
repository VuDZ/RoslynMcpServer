"""Task-00.3 documentation and ownership checks; no C# execution claims."""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from urllib.parse import unquote

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
TOPIC = ROOT / 'docs/workspace-state-cache'
EPOCH = TOPIC / 'epoch-1-disk-cache-mvp'
BASELINE = json.loads((HERE / 'baseline.json').read_text(encoding='utf-8'))
BOOKKEEPING = {
    'docs/README.md', 'docs/workspace-state-cache/README.md',
    'docs/workspace-state-cache/execution.md',
    'docs/workspace-state-cache/epoch-1-disk-cache-mvp/README.md',
    'docs/workspace-state-cache/epoch-1-disk-cache-mvp/task-00.3-manifest-contract-scope-hi.md',
    'docs/workspace-state-cache/epoch-1-disk-cache-mvp/evidence/task-00-alternatives-preparation.md',
}


def read(path):
    return path.read_text(encoding='utf-8-sig')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def graph():
    result = {}
    for path in sorted(TOPIC.glob('epoch-*/task-*.md')):
        value = re.search(r'^- Depends on: (.+)$', read(path), re.M).group(1)
        result[path.relative_to(ROOT).as_posix()] = [
            (path.parent / target).resolve().relative_to(ROOT).as_posix()
            for target in re.findall(r'\]\(([^)]+)\)', value)
        ]
    return result


parser = argparse.ArgumentParser()
parser.add_argument('--baseline', action='store_true')
args = parser.parse_args()
current = graph()
if args.baseline:
    (HERE / 'dependencies-before.json').write_text(
        json.dumps(current, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'tasks': len(current), 'edges': sum(map(len, current.values()))}))
    raise SystemExit(0)

errors = []
reviewed_report_hash = '75C65929DB673543FD39DE6C6223C78497B030CD09F3CA8E3A6187048C7D2575'
if digest(EPOCH / 'evidence/task-00.3-contract-scope.md') != reviewed_report_hash:
    errors.append('Contract report changed after independent ACCEPT')
old_graph = json.loads((HERE / 'dependencies-before.json').read_text(encoding='utf-8'))
if current != old_graph:
    errors.append('Task inventory or dependency edges changed')
active, visited = set(), set()


def visit(node):
    if node in active:
        errors.append('Dependency cycle: ' + node)
        return
    if node in visited:
        return
    active.add(node)
    for target in current[node]:
        if target not in current:
            errors.append('Missing dependency: ' + target)
        else:
            visit(target)
    active.remove(node)
    visited.add(node)


for node in current:
    visit(node)

protected = BASELINE['protected_contracts'] + [
    item for item in BASELINE['dirty_files'] if item['Path'] not in BOOKKEEPING
]
for item in protected:
    path = ROOT / item['Path']
    if not path.is_file() or digest(path) != item['Hash']:
        errors.append('Protected baseline changed: ' + item['Path'])

head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
if head != BASELINE['head']:
    errors.append('HEAD changed; no-commit requirement violated')

changed = set(subprocess.check_output(
    ['git', '-c', 'core.safecrlf=false', 'diff', '--name-only', 'HEAD'], cwd=ROOT, text=True).splitlines())
changed.update(subprocess.check_output(
    ['git', 'ls-files', '--others', '--exclude-standard'], cwd=ROOT, text=True).splitlines())
prior = {item['Path'] for item in BASELINE['dirty_files']}
new_paths = changed - prior
owned_prefix = 'docs/workspace-state-cache/epoch-1-disk-cache-mvp/evidence/task-00.3'
unexpected = sorted(path for path in new_paths if path not in BOOKKEEPING and not (
    path.startswith(owned_prefix) and Path(path).suffix in {'.md', '.py', '.json'}))
if unexpected:
    errors.append('Unexpected new changes: ' + ', '.join(unexpected))

files = [ROOT / item for item in BOOKKEEPING]
files += sorted(EPOCH.glob('evidence/task-00.3*.md'))
link_count = 0
anchor_count = 0


def anchors(path):
    result, counts = set(), {}
    for heading in re.findall(r'^#{1,6}\s+(.+)$', read(path), re.M):
        value = ''.join(c.lower() for c in heading if c.isalnum() or c.isspace() or c in '-_').replace(' ', '-')
        count = counts.get(value, 0)
        result.add(value + ('-' + str(count) if count else ''))
        counts[value] = count + 1
    return result


for path in files:
    text = re.sub(r'(?s)```.*?```', '', read(path))
    for target in re.findall(r'\]\(([^)]+)\)', text):
        if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', target):
            continue
        target_file = unquote(target.split('#', 1)[0])
        destination = (path.parent / target_file).resolve() if target_file else path
        link_count += 1
        if not destination.is_file():
            errors.append(f'{path.relative_to(ROOT)}: missing target {target}')
        elif '#' in target and destination.suffix == '.md':
            anchor_count += 1
            if unquote(target.split('#', 1)[1]) not in anchors(destination):
                errors.append(f'{path.relative_to(ROOT)}: missing anchor {target}')
    for number, line in enumerate(read(path).splitlines(), 1):
        if line.rstrip(' \t') != line:
            errors.append(f'{path.relative_to(ROOT)}:{number}: trailing whitespace')

task = read(EPOCH / 'task-00.3-manifest-contract-scope-hi.md')
for field in ('Статус', 'Модель, реализовавшая задачу', 'Модели, проводившие ревью',
              'Количество раундов ревью', 'Количество исправлений после ревью',
              'Отчёт о реализации, ревью и validation'):
    if not re.search(r'^- ' + field + r': .+', task, re.M):
        errors.append('Missing bookkeeping field: ' + field)
if 'task-00.3-validation.md' not in task or 'accepted' not in task:
    errors.append('Task acceptance evidence missing')
for path in [EPOCH / 'README.md', TOPIC / 'README.md', TOPIC / 'execution.md', ROOT / 'docs/README.md']:
    if 'task-00.3-validation.md' not in read(path):
        errors.append('Summary lacks acceptance link: ' + str(path.relative_to(ROOT)))

diff = subprocess.run(['git', '-c', 'core.safecrlf=false', 'diff', '--check'],
                      cwd=ROOT, capture_output=True, text=True)
if diff.returncode:
    errors.append('git diff --check: ' + diff.stdout + diff.stderr)

result = {
    'date': '2026-10-09', 'scope': 'docs-only task-00.3 contract audit; draft not applied',
    'head': head, 'tasks': len(current), 'dependency_edges': sum(map(len, current.values())),
    'dependencies_unchanged': current == old_graph,
    'reviewed_report_sha256': reviewed_report_hash,
    'local_links_checked': link_count, 'local_anchors_checked': anchor_count, 'documents_checked': len(files),
    'protected_baseline_checks': len(protected), 'git_diff_check_exit': diff.returncode,
    'new_owned_paths': sorted(new_paths), 'unexpected_new_paths': unexpected,
    'production_test_config_changes_by_task': 0,
    'build_main_source_structure_lifecycle': 'not-run / not required for docs-only scope',
    'errors': errors, 'status': 'passed' if not errors else 'failed',
}
(HERE / 'checks.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(bool(errors))
