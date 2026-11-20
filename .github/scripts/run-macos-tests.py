import os
import signal
import subprocess
import sys
from contextlib import suppress
from pathlib import Path


def capture_processes(group_id, results):
    diagnostics = results / "watchdog"
    diagnostics.mkdir(parents=True, exist_ok=True)
    process_list = subprocess.run(
        ["ps", "-axww", "-o", "pid=", "-o", "ppid=", "-o", "pgid=", "-o", "stat=", "-o", "etime=", "-o", "command="],
        capture_output=True, text=True, check=True, timeout=5,
    )
    members = []
    for line in process_list.stdout.splitlines():
        fields = line.split(maxsplit=5)
        if len(fields) == 6 and fields[2] == str(group_id):
            pid, _, _, state, _, command = fields
            members.append((pid, state, command, line))
    (diagnostics / "processes.txt").write_text(
        "PID PPID PGID STAT ELAPSED COMMAND\n" + "\n".join(member[3] for member in members) + "\n",
        encoding="utf-8",
    )

    candidates = sorted(members, key=lambda member: "testhost" not in member[2].lower())
    targets = [
        pid for pid, state, command, _ in candidates
        if not state.startswith("Z") and ("dotnet" in command.lower() or "testhost" in command.lower())
    ]
    for pid in targets[:6]:
        report = diagnostics / f"sample-{pid}.txt"
        with (diagnostics / f"sample-{pid}.log").open("w", encoding="utf-8") as log:
            try:
                subprocess.run(
                    ["/usr/bin/sample", pid, "2", "10", "-file", str(report)],
                    stdout=log, stderr=subprocess.STDOUT, check=True, timeout=8,
                )
            except (OSError, subprocess.SubprocessError) as error:
                print(error, file=log)


def run(command, results, deadline):
    process = subprocess.Popen(command, start_new_session=True)
    try:
        return process.wait(timeout=deadline)
    except subprocess.TimeoutExpired:
        print(f"VSTest exceeded {deadline} seconds. Capturing process diagnostics before termination.", flush=True)
        try:
            capture_processes(process.pid, results)
        except (OSError, subprocess.SubprocessError) as error:
            print(f"Process diagnostics failed: {error}", file=sys.stderr, flush=True)
        return 124
    finally:
        with suppress(ProcessLookupError):
            os.killpg(process.pid, signal.SIGKILL)
        with suppress(subprocess.TimeoutExpired):
            process.wait(timeout=5)


if __name__ == "__main__":
    sys.exit(run(sys.argv[1:], Path(os.environ["RESULTS_DIRECTORY"]), deadline=600))
