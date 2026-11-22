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
    prov = str(t.get("env", {}).get("Payments__Provider", "")).strip().lower()
    if prov == "mock": fail(f, "Payments__Provider must not be Mock in a deployed config (security review F-01: Mock is the fake profile's and Booking refuses to start with it)")
    if stem == "web-blazor":
        # ADR-0010 "Hosting", BLAZOR-WEB-APP section 14: a stateful frontend. These are the settings a circuit's survival depends on.
        hs = hs or {}
        if hs.get("min_machines_running") != 1: fail(f, "min_machines_running must be 1 (a stopped machine ends every circuit; no scale to zero)")
        if hs.get("auto_stop_machines") not in ("off", False): fail(f, 'auto_stop_machines must be "off"')
        if not any(c.get("path") == "/alive" for c in hs.get("checks", [])): fail(f, "the Fly check must ask /alive")
        mounts = t.get("mounts")
        dest = (mounts[0] if isinstance(mounts, list) and mounts else mounts or {}).get("destination")
        keys = str(t.get("env", {}).get("DataProtection__KeysPath", ""))
        if not dest: fail(f, "a [mounts] volume is required for the Data Protection keys")
        elif not (keys == dest or keys.startswith(dest.rstrip("/") + "/")): fail(f, f"DataProtection__KeysPath {keys!r} is not on the mounted volume {dest!r}")
        if str(t.get("env", {}).get("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "")).lower() != "true": fail(f, "ASPNETCORE_FORWARDEDHEADERS_ENABLED must be true (the scheme behind Fly's proxy; the origin guard)")
        if not str(t.get("env", {}).get("Site__BaseUrl", "")).startswith("https://"): fail(f, "Site__BaseUrl must be an https URL (cookies are Secure only then; the origin guard allows exactly it)")
        if "processes" in t or t.get("http_service", {}).get("processes"): fail(f, "one process only (one machine, one session store)")
        mem = str(t["vm"][0].get("memory", "")) if isinstance(t.get("vm"), list) and t["vm"] else ""
        if not mem: fail(f, "[[vm]] memory must be stated (the budget is in PERFORMANCE.md)")
    print(f"ok   {f}")
if len(files) < 7: fail("deploy/fly", f"expected 7 *.fly.toml (SPEC 4.3), found {len(files)}")
sys.exit(1 if bad else 0)
PY
