#!/usr/bin/env python3
"""Validate that service versions and gateway release metadata agree."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent.parent
version = json.loads((root / "version.json").read_text())
manifest = json.loads((root / "Dhole.ApiGateway/service-versions.json").read_text())
assert manifest["releaseId"] == version["releaseId"]
assert manifest["ecosystemVersion"] == version["ecosystemVersion"]
assert next(s for s in manifest["services"] if s["key"] == "gateway")["version"] == version["version"]
assert len(manifest["services"]) >= 14
assert len({s["key"] for s in manifest["services"]}) == len(manifest["services"])
assert all(re.fullmatch(r"[0-9]+[.][0-9]+[.][0-9]+", s["version"]) for s in manifest["services"])
assert 'app.MapGet("/api/system/versions"' in (root / "Dhole.ApiGateway/Program.cs").read_text()
assert 'Content Update="service-versions.json"' in (root / "Dhole.ApiGateway/Dhole.ApiGateway.csproj").read_text()
print("PASS gateway registry")
