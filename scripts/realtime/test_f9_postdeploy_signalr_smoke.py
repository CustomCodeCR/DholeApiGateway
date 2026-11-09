#!/usr/bin/env python3
"""CI-only negative tests for post-staging-deploy SignalR reachability check."""
import importlib.util
from pathlib import Path
import unittest

modpath = Path(__file__).with_name("f9_postdeploy_signalr_smoke.py")
spec=importlib.util.spec_from_file_location("f9_postdeploy",modpath)
mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)

class F9PostDeploySmokeTests(unittest.TestCase):
    def test_anonymous_unauthorized_but_present(self):
        self.assertEqual([],mod.evaluate(200,401,403))
    def test_anonymous_forbidden_but_present(self):
        self.assertEqual([],mod.evaluate(200,403,401))
    def test_universal_hub_absent_is_failure(self):
        self.assertNotEqual([],mod.evaluate(200,404,404))
    def test_unauthorized_not_redirected_to_login(self):
        self.assertNotEqual([],mod.evaluate(200,302,401))
    def test_anonymous_access_insecure(self):
        self.assertNotEqual([],mod.evaluate(200,200,101))
    def test_gateway_health_unavailable(self):
        self.assertNotEqual([],mod.evaluate(503,401,401))

if __name__ == "__main__":unittest.main()
