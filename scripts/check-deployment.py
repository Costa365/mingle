"""Functional assertions against the actual Nginx/API deployment."""

import json
import re
import sys
import urllib.error
import urllib.request

base = sys.argv[1].rstrip("/")


def get(path):
    try:
        response = urllib.request.urlopen(base + path, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, response.headers, response.read().decode()


for route in ["/api/health/live", "/api/health/ready"]:
    status, headers, body = get(route)
    assert status == 200 and json.loads(body) == {"status": "healthy"}, route
    assert headers["Cache-Control"] == "no-store", route

status, headers, body = get("/api/games")
assert status == 200 and len(json.loads(body)) == 5
assert headers["Cache-Control"] == "no-store"
for route in ["/api/missing", "/hubs/missing"]:
    status, headers, body = get(route)
    assert status == 404 and "<html" not in body.lower(), route
    assert headers["Cache-Control"] == "no-store", route

for route in ["/", "/games/this-or-that", "/how-to-play"]:
    status, headers, body = get(route)
    assert status == 200 and '<div id="root">' in body, route
    assert headers["Cache-Control"] == "no-cache", route
    assert "frame-ancestors 'none'" in headers["Content-Security-Policy"]

asset = re.search(r'src="(/assets/[^\"]+\.js)"', get("/")[2]).group(1)
status, headers, _ = get(asset)
assert status == 200 and "immutable" in headers["Cache-Control"]
assert get("/assets/missing.js")[0] == 404
print("Proxy routing, health, API errors, SPA deep links, CSP, and cache headers passed.")
