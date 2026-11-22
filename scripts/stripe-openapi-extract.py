#!/usr/bin/env python3
"""Extracts the request fields and object properties the Stripe adapter uses from Stripe's OpenAPI spec (stripe/openapi, latest/openapi.spec3.json)
into tests/Gabinety.Booking.Tests/Payments/Stripe/Fixtures/openapi-fields.json, so a test can check the adapter's field names against the spec without the 5 MB file.
Usage: python3 -I scripts/stripe-openapi-extract.py <path to openapi.spec3.json>   (T-09.05)"""
import json, sys

spec = json.load(open(sys.argv[1]))
comp = spec["components"]["schemas"]

def deref(s):
    while isinstance(s, dict) and "$ref" in s:
        s = comp[s["$ref"].split("/")[-1]]
    return s

def flatten(schema, prefix, out, depth=0):
    schema = deref(schema)
    if depth > 6 or not isinstance(schema, dict):
        return
    if "anyOf" in schema:  # a field that is either an object or a scalar: keep every branch
        for b in schema["anyOf"]:
            flatten(b, prefix, out, depth + 1)
    t = schema.get("type")
    if t == "array":
        out.add(prefix + "[]")
        flatten(schema.get("items", {}), prefix + "[]", out, depth + 1)
    props = schema.get("properties")
    if props:
        for k, v in props.items():
            p = f"{prefix}.{k}" if prefix else k
            out.add(p)
            flatten(v, p, out, depth + 1)
    elif schema.get("additionalProperties") and prefix:
        out.add(prefix + ".*")

def request_fields(path):
    s = spec["paths"][path]["post"]["requestBody"]["content"]["application/x-www-form-urlencoded"]["schema"]
    out = set(); flatten(s, "", out); return sorted(out)

result = {
    "specVersion": spec["info"]["version"],
    "requests": {p: request_fields(p) for p in ["/v1/checkout/sessions", "/v1/refunds", "/v1/transfers", "/v1/accounts", "/v1/account_links"]},
    "objects": {n: sorted(comp[n]["properties"].keys()) for n in ["checkout.session", "refund", "transfer", "account", "account_link", "event"]},
    "events": sorted(e for e in {x for x in json.dumps(spec).split('"')} if e.startswith(("checkout.session.", "refund.", "account.updated"))),
}
json.dump(result, open("tests/Gabinety.Booking.Tests/Payments/Stripe/Fixtures/openapi-fields.json", "w"), indent=1)
print(result["specVersion"], {k: len(v) for k, v in result["requests"].items()})
