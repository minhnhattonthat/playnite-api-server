"""Structural validation of the OpenAPI 3.0.3 document served at /openapi.json."""
import pytest


@pytest.fixture(scope="module")
def spec(api, base_url):
    r = api.get(f"{base_url}/api/openapi.json")
    r.raise_for_status()
    return r.json()


# ─── Top-level shape ──────────────────────────────────────────────────


def test_openapi_version(spec):
    assert spec.get("openapi") == "3.0.3"


def test_has_info_block(spec):
    info = spec.get("info")
    assert info is not None
    assert "title" in info
    assert "version" in info


def test_has_servers_block(spec):
    servers = spec.get("servers")
    assert isinstance(servers, list) and len(servers) > 0
    assert servers[0].get("url") == "/api"


# ─── Components ───────────────────────────────────────────────────────


def test_has_expected_schemas(spec):
    schemas = spec.get("components", {}).get("schemas", {})
    expected = {
        "Error", "Game", "GameCreate", "GamePage",
        "NamedItem", "NamedItemCreate", "Platform",
    }
    missing = expected - set(schemas.keys())
    assert not missing, f"Missing component schemas: {missing}"


def test_has_bearer_security_scheme(spec):
    schemes = spec.get("components", {}).get("securitySchemes", {})
    assert "bearerAuth" in schemes
    bearer = schemes["bearerAuth"]
    assert bearer.get("type") == "http"
    assert bearer.get("scheme") == "bearer"


# ─── Path coverage ────────────────────────────────────────────────────


def test_has_expected_paths(spec):
    # Paths are relative to servers[0].url (= "/api"), so the entries here
    # are unprefixed. Full URL is "/api" + path.
    paths = spec.get("paths", {})
    expected = {
        "/health",
        "/games",
        "/games/{id}",
        "/games/{id}/media/{kind}",
        "/platforms", "/platforms/{id}",
        "/genres", "/genres/{id}",
        "/companies", "/companies/{id}",
        "/features", "/features/{id}",
        "/categories", "/categories/{id}",
        "/tags", "/tags/{id}",
        "/series", "/series/{id}",
        "/ageratings", "/ageratings/{id}",
        "/regions", "/regions/{id}",
        "/sources", "/sources/{id}",
        "/completionstatuses", "/completionstatuses/{id}",
        "/emulators", "/emulators/{id}",
    }
    missing = expected - set(paths.keys())
    assert not missing, f"Missing paths: {missing}"


# ─── GET /games parameter surface ─────────────────────────────────────


EXPECTED_GAMES_PARAMS = {
    # pagination (existing)
    "offset", "limit", "q",
    # boolean filters
    "isInstalled", "favorite", "hidden",
    # single-ID filters
    "sourceId", "completionStatusId",
    # multi-ID filters
    "platformIds", "genreIds", "developerIds", "publisherIds",
    "categoryIds", "tagIds", "featureIds",
    # ranges
    "playtimeMin", "playtimeMax", "userScoreMin",
    "lastActivityAfter", "lastActivityBefore",
    # sort
    "sort",
}


def test_games_list_has_21_parameters(spec):
    op = spec["paths"]["/games"]["get"]
    params = op.get("parameters", [])
    names = {p["name"] for p in params}
    assert names == EXPECTED_GAMES_PARAMS, (
        f"Missing: {EXPECTED_GAMES_PARAMS - names}, "
        f"Unexpected: {names - EXPECTED_GAMES_PARAMS}"
    )


# ─── Security carve-out ───────────────────────────────────────────────


# Unprefixed because spec paths are relative to servers[0].url = "/api".
# Documentation routes are currently registered after the spec is built
# (so they don't appear in paths at all), but this set is correct if that
# ever changes.
ANONYMOUS_PATHS = {
    "/docs", "/openapi.json",
    "/swagger-ui.css",
    "/swagger-ui-bundle.js",
    "/swagger-ui-standalone-preset.js",
    "/favicon.png",
}

_HTTP_METHODS = {"get", "post", "put", "patch", "delete", "head", "options"}


def test_non_anonymous_routes_require_bearer(spec):
    """Every non-anonymous operation must declare bearerAuth in its security."""
    for path, path_item in spec["paths"].items():
        if path in ANONYMOUS_PATHS:
            continue
        for method, op in path_item.items():
            if method not in _HTTP_METHODS:
                continue
            security = op.get("security", [])
            assert security, f"{method.upper()} {path} has no security array"
            assert any("bearerAuth" in req for req in security), (
                f"{method.upper()} {path} does not require bearerAuth"
            )


def test_anonymous_routes_have_empty_security(spec):
    """Anonymous routes have security: [] (explicit empty array overrides any
    global default)."""
    for path in ANONYMOUS_PATHS:
        if path not in spec["paths"]:
            continue
        for method, op in spec["paths"][path].items():
            if method not in _HTTP_METHODS:
                continue
            security = op.get("security")
            assert security == [], (
                f"{method.upper()} {path} security should be [], got {security}"
            )
