#!/usr/bin/env python3
"""CI-only tests for the F9 staging postdeploy HTTPS/WSS probe."""
import importlib.util
from pathlib import Path
from types import SimpleNamespace
from unittest import TestCase, main, mock

modpath = Path(__file__).with_name("f9_postdeploy_signalr_smoke.py")
spec = importlib.util.spec_from_file_location("f9_postdeploy", modpath)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class F9PostDeploySmokeTests(TestCase):
    def test_anonymous_unauthorized_but_present(self):
        self.assertEqual([], mod.evaluate(200, 401, 403))

    def test_anonymous_forbidden_but_present(self):
        self.assertEqual([], mod.evaluate(200, 403, 401))

    def test_hub_absent_is_failure(self):
        self.assertNotEqual([], mod.evaluate(200, 404, 404))

    def test_unauthorized_not_redirected_to_login(self):
        self.assertNotEqual([], mod.evaluate(200, 302, 401))

    def test_anonymous_access_insecure(self):
        self.assertNotEqual([], mod.evaluate(200, 200, 101))

    def test_gateway_health_unavailable(self):
        self.assertNotEqual([], mod.evaluate(403, 401, 401))
        self.assertNotEqual([], mod.evaluate(503, 401, 401))

    def test_curl_https_probe_preserves_cert_validation_and_http_status(self):
        with mock.patch.object(
            mod.subprocess, "run",
            return_value=SimpleNamespace(returncode=0, stdout="200", stderr="")
        ) as run:
            self.assertEqual(200, mod.status(mod.ORIGIN + "/health"))
            args = run.call_args.args[0]
            self.assertEqual("curl", args[0])
            self.assertNotIn("--insecure", args)
            self.assertNotIn("--location", args)

    def test_curl_websocket_probe_uses_http11_upgrade_headers(self):
        with mock.patch.object(
            mod.subprocess, "run",
            return_value=SimpleNamespace(returncode=0, stdout="401", stderr="")
        ) as run:
            self.assertEqual(401, mod.status(mod.ORIGIN + "/api/notifications/hub", upgrade=True))
            args = run.call_args.args[0]
            self.assertIn("--http1.1", args)
            self.assertIn("Upgrade: websocket", args)

    def test_transport_error_never_counts_as_success(self):
        with mock.patch.object(
            mod.subprocess, "run",
            return_value=SimpleNamespace(returncode=28, stdout="000", stderr="Timeout")
        ):
            with self.assertRaises(OSError):
                mod.status(mod.ORIGIN + "/health")


if __name__ == "__main__":
    main()
