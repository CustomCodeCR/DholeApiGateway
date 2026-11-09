#!/usr/bin/env python3
"""F9 read-only staging SignalR smoke, executed after Gateway staging deployment.

Use the same curl-based HTTPS probe as the DholeSystem staging infrastructure
workflow. Python urllib's default client was receiving edge HTTP 403 despite
the staging infrastructure curl probe observing HTTP 200 on the same host.

This proves only public route reachability and anonymous denial; it does NOT
prove authenticated WSS delivery, resource-owner ACL, or formal F3/F6 approval.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import time

ORIGIN = "https://dhole-api.customcodecr.com"


def evaluate(health: int, negotiate: int, websocket: int) -> list[str]:
    problems = []
    if health != 200:
        problems.append(f"HTTPS health unexpected HTTP {health}")
    if negotiate not in (401, 403):
        problems.append(f"Hub negotiate expected auth denial (401/403), got {negotiate}")
    if websocket not in (401, 403):
        problems.append(f"Unauth WSS upgrade expected denial (401/403), got {websocket}")
    return problems


def status(url: str, method: str = "GET", upgrade: bool = False) -> int:
    # No redirects, insecure TLS flags, proxy bypass, credentials, or tokens.
    # --http1.1 permits a genuine WebSocket Upgrade request through the edge.
    command = [
        "curl", "--silent", "--show-error", "--connect-timeout", "5",
        "--max-time", "12", "--output", "/dev/null",
        "--write-out", "%{http_code}", "--request", method,
        "--header", "Cache-Control: no-store",
    ]
    if upgrade:
        command.extend([
            "--http1.1",
            "--header", "Connection: Upgrade",
            "--header", "Upgrade: websocket",
            "--header", "Sec-WebSocket-Version: 13",
            "--header", "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
        ])
    command.append(url)
    result = subprocess.run(command, capture_output=True, text=True, check=False)
    raw_status = result.stdout.strip()
    if result.returncode != 0 or not raw_status.isdecimal():
        raise OSError(f"curl probe failed: exit={result.returncode}")
    return int(raw_status)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="F9Results/f9-staging-signalr-postdeploy.json")
    args = parser.parse_args()
    record = {
        "releaseId": "rt-20261109-f9-hotfix-01",
        "environment": "staging",
        "scope": "POST_DEPLOY_READ_ONLY_ANONYMOUS",
        "origin": ORIGIN,
        "observedAtUtc": datetime.now(timezone.utc).isoformat(),
        "authorizedWssDeliveryVerified": False,
        "realOwnerAclVerified": False,
        "formalF3Approved": False,
        "f10Authorized": False,
    }
    try:
        # The container may have started seconds before the public route is ready.
        for attempt in range(5):
            record["healthHttpStatus"] = status(ORIGIN + "/health")
            if record["healthHttpStatus"] == 200:
                break
            if attempt < 4:
                time.sleep(2)
        record["anonymousHubNegotiateStatus"] = status(
            ORIGIN + "/api/notifications/hub/negotiate?negotiateVersion=1", "POST"
        )
        record["anonymousWebsocketUpgradeStatus"] = status(
            ORIGIN + "/api/notifications/hub", "GET", True
        )
        record["problems"] = evaluate(
            record["healthHttpStatus"],
            record["anonymousHubNegotiateStatus"],
            record["anonymousWebsocketUpgradeStatus"],
        )
    except OSError as error:
        record["problems"] = ["Transport or TLS connection failed: " + type(error).__name__]
    record["transportRouteReady"] = not record["problems"]
    path = Path(args.output)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    if record["problems"]:
        print("F9_STAGING_SIGNALR_POSTDEPLOY_BLOCKED: " + "; ".join(record["problems"]))
        return 1
    print(
        "NOTIFICATIONS_HUB_STAGING_ROUTE_READY: TLS proxy route and anonymous "
        "authorization; authenticated WSS/owner ACL still unverified"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
