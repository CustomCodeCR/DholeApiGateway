#!/usr/bin/env python3
"""F9 read-only signalR route smoke: run ONLY after staging deployment.

It probes TLS reachability and unauthenticated denial at the existing NotificationsHub proxy. It does
NOT prove authorized WSS delivery, owner permissions or formal production acceptance.
No QA JWT, SSH, server writes, flags or secrets needed.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import ssl
import urllib.request
import urllib.error

ORIGIN = "https://dhole-api.customcodecr.com"

def evaluate(health: int, negotiate: int, websocket: int) -> list[str]:
    problems = []
    if health != 200: problems.append(f"HTTPS health unexpected HTTP {health}")
    if negotiate not in (401, 403): problems.append(f"Hub negotiate expected auth denial (401/403), got {negotiate}")
    if websocket not in (401, 403): problems.append(f"Unauth WSS upgrade expected denial (401/403), got {websocket}")
    return problems

def status(url: str, method: str = "GET", upgrade: bool = False) -> int:
    headers = {"Cache-Control": "no-store"}
    if upgrade:
        headers.update({"Connection": "Upgrade", "Upgrade": "websocket",
                        "Sec-WebSocket-Version": "13",
                        "Sec-WebSocket-Key": "dGhlIHNhbXBsZSBub25jZQ=="})
    req = urllib.request.Request(url, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=10, context=ssl.create_default_context()) as resp:
            return resp.status
    except urllib.error.HTTPError as error:
        return error.code

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="F9Results/f9-staging-signalr-postdeploy.json")
    args = parser.parse_args()
    record = {"releaseId":"rt-20261107-01","environment":"staging",
              "scope":"POST_DEPLOY_READ_ONLY_ANONYMOUS","origin":ORIGIN,
              "observedAtUtc":datetime.now(timezone.utc).isoformat(),
              "authorizedWssDeliveryVerified":False,"realOwnerAclVerified":False,
              "formalF3Approved":False,"f10Authorized":False}
    try:
        record["healthHttpStatus"] = status(ORIGIN + "/health")
        record["anonymousHubNegotiateStatus"] = status(ORIGIN + "/api/notifications/hub/negotiate?negotiateVersion=1", "POST")
        record["anonymousWebsocketUpgradeStatus"] = status(ORIGIN.replace("https://","https://") + "/api/notifications/hub", "GET", True)
        record["problems"] = evaluate(record["healthHttpStatus"],
                                      record["anonymousHubNegotiateStatus"],
                                      record["anonymousWebsocketUpgradeStatus"])
    except (OSError, ValueError) as error:
        record["problems"] = ["Transport or TLS connection failed: " + type(error).__name__]
    record["transportRouteReady"] = not record["problems"]
    path = Path(args.output)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(record, indent=2)+"\n",encoding="utf-8")
    if record["problems"]:
        print("F9_STAGING_SIGNALR_POSTDEPLOY_BLOCKED: "+"; ".join(record["problems"]))
        return 1
    print("NOTIFICATIONS_HUB_STAGING_ROUTE_READY: TLS proxy route and anonymous authorization; owner ACL still unverified")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
