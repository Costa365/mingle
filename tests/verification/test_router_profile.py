"""Deployment contract checks; real proxy/API/browser checks run in CI separately."""

import json
import os
from pathlib import Path
import re
import subprocess
import unittest
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[2]


class RouterProfileTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        environment = dict(os.environ, MINGLE_ADMIN_CREDENTIAL="",
                           MINGLE_NETWORK_SUBNET="172.30.96.0/24",
                           MINGLE_API_IP="172.30.96.2", MINGLE_WEB_IP="172.30.96.3")
        result = subprocess.run(
            ["docker", "compose", "-f", "docker-compose-prod.yml", "config", "--format", "json"],
            cwd=ROOT, env=environment, capture_output=True, text=True, check=True)
        cls.config = json.loads(result.stdout)

    def test_only_frontend_joins_shared_router_and_publishes_loopback_8085(self):
        api, web = (self.config["services"][name] for name in ("api", "web"))
        self.assertNotIn("ports", api)
        self.assertEqual(set(api["networks"]), {"default"})
        self.assertEqual(set(web["networks"]), {"default", "nginx-proxy"})
        self.assertEqual(web["ports"][0]["host_ip"], "127.0.0.1")
        self.assertEqual(str(web["ports"][0]["published"]), "8085")
        self.assertEqual(web["ports"][0]["target"], 8080)
        self.assertTrue(self.config["networks"]["nginx-proxy"]["external"])

    def test_secure_origin_and_trusted_peer_match_frontend(self):
        api, web = (self.config["services"][name] for name in ("api", "web"))
        settings = api["environment"]
        self.assertEqual(settings["Security__AllowInsecureCookies"], "false")
        self.assertEqual(settings["Security__PublicOrigin"], "https://mingle.costa365.site")
        self.assertEqual(settings["AllowedHosts"].split(";"),
                         [web["environment"]["VIRTUAL_HOST"], "localhost", "127.0.0.1"])
        self.assertEqual(settings["Proxy__KnownProxy"], web["networks"]["default"]["ipv4_address"])
        self.assertEqual(web["environment"]["VIRTUAL_PORT"], "8080")
        self.assertEqual(api["volumes"][0]["target"], "/data")

    def test_production_and_rehearsal_allow_the_actual_image_healthcheck_host(self):
        dockerfile = (ROOT / "infra/api.Dockerfile").read_text()
        probe_url = re.search(r"http://[^\s]+/api/health/ready", dockerfile).group()
        probe_host = urlparse(probe_url).hostname
        self.assertIn(probe_host, self.config["services"]["api"]["environment"]["AllowedHosts"].split(";"))
        rehearsal = (ROOT / "scripts/verify-router.sh").read_text()
        allowed_hosts = re.search(r'AllowedHosts: "([^"]+)"', rehearsal).group(1)
        self.assertIn(probe_host, allowed_hosts.split(";"))

    def test_router_profile_overwrites_forwarding_without_changing_direct_profiles(self):
        router = (ROOT / "infra/nginx-router.conf").read_text()
        server = (ROOT / "infra/nginx-server.conf").read_text()
        maps = (ROOT / "infra/nginx-maps.conf").read_text()
        self.assertIn("set $mingle_forwarded_proto https;", router)
        self.assertIn("map $scheme $mingle_forwarded_proto", maps)
        self.assertIn("default $scheme;", maps)
        self.assertIn("proxy_set_header X-Forwarded-Proto $mingle_forwarded_proto;", server)
        self.assertIn("proxy_set_header X-Forwarded-For $remote_addr;", server)
        self.assertNotIn("$http_x_forwarded", server + router)
