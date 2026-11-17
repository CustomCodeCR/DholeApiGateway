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
assert len(manifest["services"]) >= 17
assert next(s for s in manifest["services"] if s["key"] == "operations")["component"] == "DholeOperationsService"
gateway_settings = json.loads((root / "Dhole.ApiGateway/appsettings.json").read_text())
routes = gateway_settings["Gateway"]["Routes"]
assert any(r["Prefix"] == "/api/operations" and r["Destination"].endswith("operations-api:8080") and not r.get("StripPrefix", False) for r in routes)
assert any(r["Prefix"] == "/hubs/operations" and r["Destination"].endswith("operations-api:8080") and not r.get("StripPrefix", False) for r in routes)
assert gateway_settings["Gateway"]["HealthChecks"]["operations"].endswith("operations-api:8080")
program = (root / "Dhole.ApiGateway/Program.cs").read_text()
assert 'service.Equals("operations", StringComparison.OrdinalIgnoreCase)' in program
assert '"/ready"' in program
assert len({s["key"] for s in manifest["services"]}) == len(manifest["services"])
assert all(re.fullmatch(r"[0-9]+[.][0-9]+[.][0-9]+", s["version"]) for s in manifest["services"])
assert 'app.MapGet("/api/system/versions"' in (root / "Dhole.ApiGateway/Program.cs").read_text()
assert 'Content Update="service-versions.json"' in (root / "Dhole.ApiGateway/Dhole.ApiGateway.csproj").read_text()
assert 'COPY ["Directory.Build.targets", "./Directory.Build.targets"]' in (root / "Dhole.ApiGateway/Dockerfile").read_text()
print("PASS gateway registry")
