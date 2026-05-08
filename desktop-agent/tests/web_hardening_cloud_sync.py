from __future__ import annotations

import hashlib
import hmac
import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path
from tempfile import TemporaryDirectory
from urllib import error, request


ROOT = Path(__file__).resolve().parents[2]


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def wait_for_server(base_url: str) -> None:
    deadline = time.time() + 8
    while time.time() < deadline:
        try:
            request.urlopen(f"{base_url}/api/auth/status", timeout=0.5).read()
            return
        except Exception:
            time.sleep(0.1)
    raise RuntimeError("server did not start")


def post_json(base_url: str, path: str, payload: dict, token: str, signature: str | None) -> tuple[int, dict]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    headers = {
        "Content-Type": "application/json; charset=utf-8",
        "Authorization": f"Bearer {token}",
        "Idempotency-Key": payload["idempotencyKey"],
    }
    if signature is not None:
        headers["X-AriadGSM-Signature"] = signature
    req = request.Request(f"{base_url}{path}", data=body, method="POST", headers=headers)
    try:
        with request.urlopen(req, timeout=3) as response:
            data = json.loads(response.read().decode("utf-8"))
            return int(response.status), data
    except error.HTTPError as exc:
        raw = exc.read().decode("utf-8", errors="replace")
        return int(exc.code), json.loads(raw or "{}")


def sign(payload: dict, token: str) -> str:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    digest = hmac.new(token.encode("utf-8"), body, hashlib.sha256).hexdigest()
    return f"sha256={digest}"


def read_audit(data_dir: Path) -> list[dict]:
    path = data_dir / "cloud-sync-audit.jsonl"
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def main() -> int:
    token = "cloud-hardening-token"
    with TemporaryDirectory() as tmp:
        data_dir = Path(tmp)
        port = free_port()
        env = {
            **os.environ,
            "DATA_DIR": str(data_dir),
            "PORT": str(port),
            "OPERATIVA_AGENT_KEY": token,
            "ARIADGSM_CLOUD_SYNC_RATE_LIMIT_PER_MINUTE": "20",
        }
        proc = subprocess.Popen(
            ["node", "server-wrapper.js"],
            cwd=ROOT,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        try:
            base_url = f"http://127.0.0.1:{port}"
            wait_for_server(base_url)
            payload = {
                "id": "hardening-batch-1",
                "idempotencyKey": "hardening-batch-1",
                "payloadHash": "hash-hardening-1",
                "schemaVersion": "cloud_sync_payload_v1",
                "actor": "desktop_agent",
                "source": "ariadgsm_local_agent",
                "mode": "local_ia",
                "status": "ok",
                "runtimeKernel": {"status": "ok", "canAct": False, "canSync": True},
                "summary": {"incidentsOpen": 0},
                "events": [],
            }

            missing_status, _ = post_json(base_url, "/api/operativa-v2/cloud/sync", payload, token, None)
            assert missing_status == 401, missing_status

            invalid_status, _ = post_json(base_url, "/api/operativa-v2/cloud/sync", payload, token, "sha256=bad")
            assert invalid_status == 401, invalid_status

            ok_status, ok_payload = post_json(
                base_url,
                "/api/operativa-v2/cloud/sync",
                payload,
                token,
                sign(payload, token),
            )
            assert ok_status == 201, ok_payload
            assert ok_payload["batch"]["idempotencyKey"] == "hardening-batch-1"

            duplicate_status, duplicate_payload = post_json(
                base_url,
                "/api/operativa-v2/cloud/sync",
                payload,
                token,
                sign(payload, token),
            )
            assert duplicate_status == 200, duplicate_payload
            assert duplicate_payload["duplicate"] is True

            audit = read_audit(data_dir)
            verdicts = [item["verdict"] for item in audit]
            assert verdicts.count("rejected") >= 2, verdicts
            assert "new" in verdicts, verdicts
            assert "duplicate" in verdicts, verdicts
            assert all(item["timestamp"] and "hash" in item for item in audit)
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()

    print("web hardening cloud sync OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
