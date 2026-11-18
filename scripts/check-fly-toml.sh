#!/usr/bin/env bash
# T-09.02: every deploy/fly/*.fly.toml parses as TOML, is named <stem> = app "gabinety-<stem>-staging", and names something that exists:
# its [build] dockerfile and context exist (paths relative to the file), and a Fly http check path appears in that Dockerfile's HEALTHCHECK.
# Image-based apps (authservice, postgres) need no Dockerfile. No network, no secrets. Needs python3 >= 3.11 (tomllib).
set -euo pipefail
cd "$(dirname "$0")/.."
python3 -I - deploy/fly/*.fly.toml <<'PY'
import sys, os, tomllib
bad = 0
def fail(f, m):
    global bad; bad += 1; print(f"FAIL {f}: {m}")
files = sys.argv[1:]
for f in files:
    try:
        with open(f, "rb") as h: t = tomllib.load(h)
    except Exception as e:
        fail(f, f"not valid TOML ({e})"); continue
    stem = os.path.basename(f)[:-len(".fly.toml")]
    if t.get("app") != f"gabinety-{stem}-staging": fail(f, f"app is {t.get('app')!r}, expected 'gabinety-{stem}-staging'")
    if t.get("primary_region") != "fra": fail(f, "primary_region must be fra (SPEC 4.3)")
    b = t.get("build", {}); here = os.path.dirname(f)
    if "image" not in b and "dockerfile" not in b: fail(f, "[build] needs image or dockerfile")
    if "dockerfile" in b:
        df = os.path.normpath(os.path.join(here, b["dockerfile"]))
        ctx = os.path.normpath(os.path.join(here, b.get("context", ".")))
        if not os.path.isfile(df): fail(f, f"dockerfile {df} does not exist")
        if not os.path.isdir(ctx): fail(f, f"context {ctx} does not exist")
        elif os.path.isfile(df):
            docker = open(df).read()
            for c in t.get("http_service", {}).get("checks", []):
                if "HEALTHCHECK" in docker and c.get("path") not in docker.split("HEALTHCHECK", 1)[1]:
                    fail(f, f"check path {c.get('path')} is not the Dockerfile HEALTHCHECK path")
    hs = t.get("http_service")
    if hs and hs.get("internal_port") != 8080 and "image" not in b: fail(f, "internal_port must be 8080")
    if hs and "min_machines_running" not in hs: fail(f, "min_machines_running must be stated (SPEC 4.3)")
    if "vm" not in t: fail(f, "[[vm]] size is missing")
    print(f"ok   {f}")
if len(files) < 7: fail("deploy/fly", f"expected 7 *.fly.toml (SPEC 4.3), found {len(files)}")
sys.exit(1 if bad else 0)
PY
