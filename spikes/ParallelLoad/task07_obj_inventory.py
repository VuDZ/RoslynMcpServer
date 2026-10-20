"""Read-only obj witnesses around ordinary and aggregator design-time loads."""
import argparse
import hashlib
import json
import pathlib


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--solution-directory", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    options = parser.parse_args()
    files = {}
    for directory in options.solution_directory.rglob("obj"):
        if not directory.is_dir():
            continue
        for path in directory.rglob("*"):
            if path.is_file():
                data = path.read_bytes()
                metadata = path.stat()
                files[path.relative_to(options.solution_directory).as_posix()] = {
                    "sha256": hashlib.sha256(data).hexdigest(), "size": len(data), "mtimeNs": metadata.st_mtime_ns}
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(files, indent=2, sort_keys=True), encoding="utf-8")
    print(f"obj inventory: {len(files)} files")


if __name__ == "__main__":
    main()
