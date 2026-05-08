from __future__ import annotations

import json
import os
import re
import socket
import subprocess
import time
from http import cookiejar
from pathlib import Path
from tempfile import TemporaryDirectory
from urllib import error, request


ROOT = Path(__file__).resolve().parents[2]


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def write_operativa_fixture(data_dir: Path) -> None:
    today = "2026-05-07"
    conversations = [
        {
            "id": f"conv-{index}",
            "channelId": f"wa-{(index % 3) + 1}",
            "title": f"Cliente {index}",
            "updatedAt": "2026-05-07T10:00:00Z",
        }
        for index in range(73)
    ]
    messages = [
        {
            "id": f"msg-{index}",
            "conversationId": f"conv-{index % 73}",
            "channelId": f"wa-{(index % 3) + 1}",
            "messageKey": f"key-{index}",
            "text": "Cuanto vale liberar Samsung en Mexico?",
            "dayKey": today,
            "sentAt": "2026-05-07T10:00:00Z",
        }
        for index in range(15705)
    ]
    messages[0]["text"] = '<img src=x onerror="alert(1)">'
    signals = [{"id": f"signal-{index}", "channelId": "wa-1"} for index in range(27066)]
    learnings = [{"id": f"learn-{index}", "customer": f"Cliente {index}", "total": "1 mensaje"} for index in range(2484)]
    payload = {
        "version": 3,
        "dayKey": today,
        "conversations": conversations,
        "messages": messages,
        "agentCheckpoints": signals,
        "weeklyAccounts": learnings,
        "reviewItems": [
            {
                "id": "review-xss",
                "title": "<script>alert(1)</script>",
                "description": '<img src=x onerror="alert(1)">',
                "status": "pendiente",
                "confidence": 0.9,
                "priority": "alta",
            }
        ],
    }
    data_dir.mkdir(parents=True, exist_ok=True)
    (data_dir / "operativa-v2.json").write_text(json.dumps(payload), encoding="utf-8")


def wait_for_server(base_url: str) -> None:
    deadline = time.time() + 8
    while time.time() < deadline:
        try:
            request.urlopen(f"{base_url}/api/auth/status", timeout=0.5).read()
            return
        except Exception:
            time.sleep(0.1)
    raise RuntimeError("server did not start")


def json_request(opener: request.OpenerDirector, url: str, payload: dict, headers: dict | None = None) -> tuple[int, dict]:
    body = json.dumps(payload).encode("utf-8")
    req = request.Request(
        url,
        data=body,
        method="POST",
        headers={"Content-Type": "application/json", **(headers or {})},
    )
    try:
        with opener.open(req, timeout=3) as response:
            return int(response.status), json.loads(response.read().decode("utf-8"))
    except error.HTTPError as exc:
        raw = exc.read().decode("utf-8", errors="replace")
        return int(exc.code), json.loads(raw or "{}")


def get_text(opener: request.OpenerDirector, url: str, headers: dict | None = None) -> tuple[int, str, object]:
    req = request.Request(url, headers=headers or {})
    try:
        response = opener.open(req, timeout=4)
        return int(response.status), response.read().decode("utf-8", errors="replace"), response
    except error.HTTPError as exc:
        return int(exc.code), exc.read().decode("utf-8", errors="replace"), exc


def assert_xss_render_contract() -> None:
    script = (ROOT / "public/operativa-v2.js").read_text(encoding="utf-8")
    assert "escapeHtml(item.text)" in script
    assert "escapeHtml(item.title)" in script
    assert "escapeHtml(item.detail || item.description)" in script
    assert "<p>${item.text}</p>" not in script
    escaped = (
        "<img src=x onerror=\"alert(1)\">"
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
        .replace("'", "&#39;")
    )
    assert escaped == "&lt;img src=x onerror=&quot;alert(1)&quot;&gt;"


def main() -> int:
    with TemporaryDirectory() as tmp:
        data_dir = Path(tmp)
        write_operativa_fixture(data_dir)
        port = free_port()
        env = {
            **os.environ,
            "DATA_DIR": str(data_dir),
            "PORT": str(port),
            "OPERATIVA_AGENT_KEY": "panel-hardening-agent-token",
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
            cookies = cookiejar.CookieJar()
            opener = request.build_opener(request.HTTPCookieProcessor(cookies))

            setup_status, _ = json_request(
                opener,
                f"{base_url}/api/auth/setup",
                {"username": "owner", "displayName": "Owner", "password": "panel-password-1"},
            )
            assert setup_status == 200, setup_status
            login_status, _ = json_request(
                opener,
                f"{base_url}/api/auth/login",
                {"username": "owner", "password": "panel-password-1"},
            )
            assert login_status == 200, login_status

            start = time.perf_counter()
            html_status, html, html_response = get_text(opener, f"{base_url}/operativa-v2.html")
            assert html_status == 200, html_status
            session_match = re.search(r'name="ariadgsm-session-token" content="([^"]+)"', html)
            csrf_match = re.search(r'name="ariadgsm-csrf-token" content="([^"]+)"', html)
            assert session_match and csrf_match
            session_token = session_match.group(1)
            csrf_token = csrf_match.group(1)

            csp = html_response.headers.get("Content-Security-Policy", "")
            assert "default-src 'self'" in csp
            assert "script-src 'self'" in csp
            assert "style-src 'self'" in csp
            assert "frame-ancestors 'none'" in csp
            assert html_response.headers.get("X-Frame-Options") == "DENY"
            assert html_response.headers.get("X-Content-Type-Options") == "nosniff"
            assert html_response.headers.get("Referrer-Policy") == "no-referrer"
            assert "max-age=31536000" in html_response.headers.get("Strict-Transport-Security", "")

            no_token_status, _, _ = get_text(opener, f"{base_url}/api/operativa-v2")
            assert no_token_status == 401, no_token_status

            headers = {"X-AriadGSM-Session": session_token}
            api_status, api_text, _ = get_text(opener, f"{base_url}/api/operativa-v2?pageSize=80", headers)
            assert api_status == 200, api_status
            api_payload = json.loads(api_text)
            assert api_payload["pagination"]["messages"]["total"] == 15705
            assert api_payload["pagination"]["messages"]["returned"] == 80
            assert api_payload["pagination"]["signals"]["total"] == 27066
            assert api_payload["pagination"]["learnings"]["total"] == 2484

            js_status, _, js_response = get_text(
                opener,
                f"{base_url}/operativa-v2.js?v=0.9.16",
                {"Accept-Encoding": "br"},
            )
            assert js_status == 200, js_status
            assert js_response.headers.get("Cache-Control") == "public, max-age=31536000, immutable"
            assert js_response.headers.get("Content-Encoding") == "br"

            duration_ms = (time.perf_counter() - start) * 1000
            assert duration_ms < 800, duration_ms

            bundle_size = (ROOT / "public/operativa-v2.js").stat().st_size
            assert bundle_size < 100 * 1024, bundle_size

            csrf_fail_status, _ = json_request(
                opener,
                f"{base_url}/api/operativa-v2/reviews/clear",
                {"actor": "panel"},
                headers,
            )
            assert csrf_fail_status == 403, csrf_fail_status

            csrf_ok_status, _ = json_request(
                opener,
                f"{base_url}/api/operativa-v2/reviews/clear",
                {"actor": "panel"},
                {**headers, "X-AriadGSM-CSRF": csrf_token},
            )
            assert csrf_ok_status == 200, csrf_ok_status
            assert_xss_render_contract()
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()

    print("web hardening panel OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
