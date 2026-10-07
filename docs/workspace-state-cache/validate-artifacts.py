"""Validate revision artifacts without modifying original specification or code."""
from pathlib import Path
from urllib.parse import unquote
import hashlib
import json
import re
import subprocess

BASE = Path(__file__).resolve().parent
ROOT = BASE.parents[1]
ORIGINAL = BASE / 'archive'
manifest = json.loads((BASE / 'input-manifest.json').read_text(encoding='utf-8'))
relocation = json.loads((BASE / 'relocation-manifest.json').read_text(encoding='utf-8'))
archive_map = {item['source']: item for item in relocation['records'] if item['kind'] == 'archive'}
errors = []

def check(condition, message):
    if not condition:
        errors.append(message)

def read(path):
    return path.read_text(encoding='utf-8-sig')

def without_code(value):
    return re.sub(r'(?s)```.*?```', '', value)

def headings(path):
    values, counts = set(), {}
    for heading in re.findall(r'^#{1,6}\s+(.+)$', without_code(read(path)), re.M):
        value = ''.join(c.lower() for c in heading if c.isalnum() or c.isspace() or c in '-_').replace(' ', '-')
        count = counts.get(value, 0)
        values.add(value + ('-'+str(count) if count else ''))
        counts[value] = count + 1
    return values

original_files = manifest['original_files']
current_original = {p.relative_to(ROOT).as_posix() for p in ORIGINAL.rglob('*')
                    if p.is_file() and p != ORIGINAL / 'index.md'}
archive_paths = {item['destination'] for item in archive_map.values()}
check(current_original == archive_paths, 'Archived original inventory changed')
check(set(archive_map) == set(original_files), 'Historical snapshot/relocation inventory mismatch')
for name, expected in original_files.items():
    item = archive_map.get(name)
    check(item is not None and item['before_sha256'] == expected, f'Original before-move snapshot mismatch: {name}')
    if item is not None:
        path = ROOT / item['destination']
        check(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == item['after_link_rebase_sha256'],
              f'Archived original changed beyond link rebasing: {name}')
        if path.suffix != '.md':
            check(item['before_sha256'] == item['after_link_rebase_sha256'], f'Historical raw snapshot/script modified: {name}')
check(len(relocation['records']) == 188, 'Moved file inventory mismatch')
for item in relocation['records']:
    check((ROOT / item['destination']).is_file(), f'Moved file missing: {item["destination"]}')

files = sorted(BASE.rglob('*.md')) + [ROOT / 'docs/README.md']
local_links = anchor_checks = 0
for path in files:
    for match in re.finditer(r'\]\(([^)]+)\)', without_code(read(path))):
        target = match.group(1)
        if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', target):
            continue
        file_part, separator, anchor = target.partition('#')
        dest = (path.parent / unquote(file_part)).resolve() if file_part else path
        local_links += 1
        check(dest.is_file(), f'{path.relative_to(ROOT)}: missing target {target}')
        if dest.is_file() and separator and dest.suffix == '.md':
            anchor_checks += 1
            check(unquote(anchor) in headings(dest), f'{path.relative_to(ROOT)}: missing anchor {target}')

fields = ['Рекомендуемые модели', 'Рекомендуемый reasoning', 'Примерная сложность',
          'Риск ошибки', 'Необходимые способности', 'Статус', 'Depends on',
          'Модель, реализовавшая задачу', 'Модели, проводившие ревью',
          'Количество раундов ревью', 'Количество исправлений после ревью',
          'Отчёт о реализации, ревью и validation']
tasks = sorted(BASE.glob('epoch-*/task-*.md'))
task_set = {t.resolve() for t in tasks}
counts, graph = {}, {}
for task in tasks:
    content, index = read(task), read(task.parent / 'README.md')
    for field in fields:
        check(f'- {field}:' in content, f'{task.name}: missing field {field}')
    for field in fields[7:]:
        check(f'- {field}: —.' in content, f'{task.name}: fabricated actual value {field}')
    check(re.search(r'^- Статус: .*planned.*2026-10-07', content, re.M), f'{task.name}: wrong status/date')
    level = task.stem.split('-')[-1]
    check(level in ('low','med','hi','xhi') and f'класс `{level}`' in content, f'{task.name}: suffix/class mismatch')
    epoch_id = 'E'+task.parent.name.split('-')[1]
    check(content.startswith('# '+epoch_id+'/task-'), f'{task.name}: wrong heading ID')
    dependency = re.search(r'^- Depends on: (.+)$', content, re.M)
    check(bool(dependency), f'{task.name}: missing dependencies')
    deptext = dependency.group(1) if dependency else ''
    graph[task.resolve()] = [(task.parent / m).resolve() for m in re.findall(r'\]\(([^)]+)\)', deptext)]
    for dep in graph[task.resolve()]:
        check(dep in task_set, f'{task.name}: dependency is not a task: {dep}')
    task_id = task.name.split('-')[1]
    entry = [line for line in index.splitlines()
             if re.match(r'^- \[task-'+task_id+r' — .*?\]\('+re.escape(task.name)+r'\)', line)]
    check(len(entry) == 1 and f'Depends on: {deptext}' in entry[0], f'{task.name}: task map dependency mismatch')
    counts[task.parent.name] = counts.get(task.parent.name,0)+1

active, visited = set(), set()
def visit(node):
    if node in active:
        errors.append(f'Dependency cycle: {node}')
        return
    if node in visited:
        return
    active.add(node)
    for dep in graph.get(node,[]):
        visit(dep)
    active.remove(node)
    visited.add(node)
for task in graph:
    visit(task)
check(len(tasks) == 35 and list(counts.values()) == [9,12,7,7], 'Task inventory/count mismatch')
for epoch in counts:
    index = read(BASE / epoch / 'README.md')
    for marker in ('Ревью плана эпохи', 'Статистика ревью задач', 'Приёмка реализации эпохи', 'revision report'):
        check(marker in index, f'{epoch}: missing bookkeeping {marker}')
    check('benchmark.md' in read(BASE / epoch / 'spec.md'), f'{epoch}: missing benchmark')

ledger = read(BASE / 'change-ledger.md')
ids = re.findall(r'^## (P-\d{3})$', ledger, re.M)
source_ids = re.findall(r'^## (P-\d{3}) ', read(ORIGINAL / 'arbitration/proposed-changes.md'), re.M)
check(ids == source_ids and len(ids) == 14, 'P inventory mismatch')
p_part = ledger.split('## Сохранённый unresolved outcome',1)[0]
check(len(re.findall(r'^Status: applied$',p_part,re.M)) == 14, 'Not all P text applied')
source_findings = set(re.findall(r'review-(?:astra|ds|grok)/F-\d{3}', read(ORIGINAL / 'arbitration/finding-registry.md')))
check(source_findings == set(re.findall(r'review-(?:astra|ds|grok)/F-\d{3}',ledger)), 'Finding provenance mismatch')
arb_ids = set(re.findall(r'ARB-\d{3}',read(ORIGINAL / 'arbitration/decision-ledger.md')))
check(arb_ids == set(re.findall(r'ARB-\d{3}',ledger)), 'Arbitration outcome coverage mismatch')
check('Strong-name вариант не выбран.' in read(BASE / 'unresolved.md'), 'Remaining U-001 no-choice state missing')
check('accepted owner decision / applied to requirements' in read(BASE / 'human-decisions.md'), 'H-001 owner decision missing')
check('S-001 и S-002 **not-run**' in read(BASE / 'spikes.md'), 'Spike open state missing')
check('35' in read(BASE / 'README.md') and ']('+'workspace-state-cache/README.md)' in read(ROOT / 'docs/README.md'), 'Root/index routing mismatch')
check(not (BASE / 'spec-v2').exists(), 'Obsolete spec-v2 directory remains')
for source in manifest['copied_source_files']:
    check((BASE / Path(source).relative_to('docs/workspace-state-cache')).is_file(), f'Original structure lost: {source}')

diff = subprocess.check_output(['git','-c','core.safecrlf=false','diff','--binary','--','.',':!docs/README.md',':!docs/workspace-state-cache'],cwd=ROOT)
check(hashlib.sha256(diff).hexdigest() == manifest['initial_tracked_diff_sha256'], 'Unrelated tracked diff changed')
whitespace = subprocess.run(['git','diff','--check','--','docs/README.md'],cwd=ROOT,capture_output=True,text=True)
check(whitespace.returncode == 0, 'Docs index whitespace failure: '+whitespace.stdout+whitespace.stderr)
own_whitespace = 0
for path in BASE.rglob('*'):
    if path.suffix not in ('.md','.py','.json') or not path.is_file():
        continue
    for number,line in enumerate(read(path).splitlines(),1):
        if line.rstrip(' \t') != line:
            own_whitespace += 1
            errors.append(f'{path.name}:{number}: trailing whitespace')

result = {'date':'2026-10-07','scope':'docs-only revision consistency; not independent review or runtime acceptance',
          'protected_original_files':len(original_files),'archived_inputs_verified':current_original == archive_paths
               and not any('snapshot mismatch:' in e or 'beyond link rebasing:' in e for e in errors),
          'historical_snapshot_policy':'original before hashes retained; archived after-link-rebase hashes checked',
          'markdown_files_checked':len(files),'local_links_checked':local_links,'anchors_checked':anchor_checks,
          'task_count':len(tasks),'epoch_task_counts':counts,'dependency_edges':sum(map(len,graph.values())),
          'acyclic':not any('cycle' in e for e in errors),'applied_P_count':len(ids),
          'source_findings_covered':len(source_findings),'arbitration_clusters_covered':len(arb_ids),
          'owner_decisions_applied':['H-001: external XML exclusion'],'unresolved':['U-001: strong-name'],'spikes_not_run':['S-001','S-002'],'new_deferred':[],
          'unrelated_tracked_diff_unchanged':hashlib.sha256(diff).hexdigest() == manifest['initial_tracked_diff_sha256'],
          'index_git_diff_check_exit':whitespace.returncode,'revision_trailing_whitespace':own_whitespace,
          'independent_recheck':'not-run','build_and_tests':'not-run; docs-only','errors':errors,
          'status':'passed' if not errors else 'failed'}
(BASE / 'validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(result,ensure_ascii=False,indent=2))
raise SystemExit(bool(errors))
