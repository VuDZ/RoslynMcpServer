"""Run as root in an isolated Linux environment; never creates or changes accounts."""
import hashlib
import json
import os
from pathlib import Path
import pwd
import stat
import subprocess
import sys
import tempfile
import time
import uuid


def main():
    assert os.geteuid() == 0, "Run this check as root so runuser can select existing UIDs."
    assembly = Path(sys.argv[1]).resolve(strict=True)
    output = Path(sys.argv[2]).resolve()
    owner_uid = int(sys.argv[3]) if len(sys.argv) > 3 else 1000
    other_uid = pwd.getpwnam("nobody").pw_uid
    assert owner_uid != other_uid and owner_uid != 0
    owner = pwd.getpwuid(owner_uid)
    key = "pr-" + uuid.uuid4().hex
    socket_path = Path(tempfile.gettempdir()) / ("CoreFxPipe_" + key)
    directory = Path(tempfile.mkdtemp(prefix="pipe-relay-access-"))
    os.chown(directory, owner_uid, owner.pw_gid)
    ready = directory / "ready"

    def drop_identity():
        os.setgroups([])
        os.setgid(owner.pw_gid)
        os.setuid(owner_uid)

    identity = hashlib.sha256()
    for path in sorted(assembly.parent.iterdir(), key=lambda p: p.name):
        if path.is_file() and (path.suffix == ".dll" or path.name == "PipeRelay"):
            identity.update((path.name + "\0").encode())
            identity.update(hashlib.sha256(path.read_bytes()).digest())
    request = json.dumps({"Version": 1, "Identity": identity.hexdigest().upper(), "Key": key,
                          "HarnessId": "cross-uid", "Generation": "00000000-0000-0000-0000-000000000000"}).encode()
    child_code = """
import json,socket,struct,sys
sock=socket.socket(socket.AF_UNIX)
sock.settimeout(3)
def read_exact(length):
    data=b''
    while len(data)<length:
        block=sock.recv(length-len(data))
        if not block:
            return None
        data+=block
    return data
try:
    sock.connect(sys.argv[1])
    data=sys.stdin.buffer.read()
    sock.sendall(struct.pack('<i',len(data))+data)
    header=read_exact(4)
    accepted=False
    if header and 0<struct.unpack('<i',header)[0]<=4096:
        body=read_exact(struct.unpack('<i',header)[0])
        reply=json.loads(body) if body else {}
        request=json.loads(data)
        accepted=(reply.get('Version')==1 and reply.get('Identity')==request['Identity']
                  and reply.get('Key')==request['Key'] and reply.get('HarnessId')=='cross-uid'
                  and reply.get('Generation') not in [None,'00000000-0000-0000-0000-000000000000'])
    print(json.dumps({'handshakeAccepted':accepted}))
except OSError as error:
    print(json.dumps({'handshakeAccepted':False,'exceptionType':type(error).__name__,'errno':error.errno}))
finally:
    sock.close()
"""

    def probe(uid):
        result = subprocess.run(["runuser", "-u", pwd.getpwuid(uid).pw_name, "--", "python3", "-c",
                                 child_code, str(socket_path)], input=request, capture_output=True, timeout=5, check=True)
        return json.loads(result.stdout)

    host = subprocess.Popen(["dotnet", str(assembly), "host", key, str(ready)],
                            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            preexec_fn=drop_identity)
    try:
        limit = time.monotonic() + 5
        while not ready.exists():
            assert host.poll() is None, "The owned host exited before readiness."
            assert time.monotonic() < limit, "Host readiness timed out."
            time.sleep(0.02)
        facts = socket_path.stat()
        assert stat.S_ISSOCK(facts.st_mode) and facts.st_uid == owner_uid
        if "--world-socket" in sys.argv:
            socket_path.chmod(0o777)
            facts = socket_path.stat()
        before, rejected, after = probe(owner_uid), probe(other_uid), probe(owner_uid)
        assert before["handshakeAccepted"] and not rejected["handshakeAccepted"] and after["handshakeAccepted"], json.dumps({"before":before,"other":rejected,"after":after,"mode":oct(stat.S_IMODE(facts.st_mode))})
        result = {"platform": "Linux", "ownerUid": owner_uid, "otherUid": other_uid,
                  "identity": identity.hexdigest().upper(),
                  "unixDomainSocketObserved": True, "socketMode": oct(stat.S_IMODE(facts.st_mode)),
                  "peerUidControl": "--world-socket" in sys.argv,
                  "ownerBefore": before, "otherUser": rejected, "ownerAfter": after, "status": "pass"}
    finally:
        if host.poll() is None:
            host.kill()
        host.communicate(timeout=5)
        assert host.poll() is not None, "The owned host survived cleanup."
        if socket_path.exists():
            assert socket_path.name == "CoreFxPipe_" + key and stat.S_ISSOCK(socket_path.stat().st_mode)
            socket_path.unlink()
        ready.unlink(missing_ok=True)
        directory.rmdir()
    result["ownedHostExited"] = True
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
