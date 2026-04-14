"""Anonymous documentation routes: /docs, /openapi.json, and Swagger UI assets."""


def test_docs_loads_anonymously(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/docs")
    assert r.status_code == 200
    assert "text/html" in r.headers.get("Content-Type", "")
    # The hand-written index.html references swagger-ui-bundle.js; verify
    # the body looks like the wrapper we ship, not a stub.
    assert "swagger-ui" in r.text.lower()
    assert "openapi.json" in r.text


def test_openapi_json_loads_anonymously(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/openapi.json")
    assert r.status_code == 200
    assert "application/json" in r.headers.get("Content-Type", "")
    body = r.json()
    assert body.get("openapi") == "3.0.3"


def test_swagger_ui_css_loads_anonymously(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/swagger-ui.css")
    assert r.status_code == 200
    assert "text/css" in r.headers.get("Content-Type", "")
    # Sanity check — the vendored CSS is ~178 KB.
    assert len(r.content) > 10_000, "CSS unexpectedly small — stub file?"


def test_swagger_ui_bundle_js_loads_anonymously(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/swagger-ui-bundle.js")
    assert r.status_code == 200
    assert "javascript" in r.headers.get("Content-Type", "")
    # Sanity check — the vendored bundle is ~1.5 MB.
    assert len(r.content) > 100_000, "Bundle unexpectedly small — stub file?"


def test_swagger_ui_preset_js_loads_anonymously(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/swagger-ui-standalone-preset.js")
    assert r.status_code == 200
    assert "javascript" in r.headers.get("Content-Type", "")
    assert len(r.content) > 10_000


def test_favicon_loads_anonymously(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/favicon.png")
    assert r.status_code == 200
    assert r.headers.get("Content-Type", "").startswith("image/png")
    assert r.content.startswith(b"\x89PNG\r\n\x1a\n"), "Body is not a PNG"
