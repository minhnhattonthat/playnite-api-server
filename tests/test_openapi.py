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
    paths = spec.get("paths", {})
    expected = {
        "/api/health",
        "/api/games",
        "/api/games/{id}",
        "/api/games/{id}/media/{kind}",
        "/api/platforms", "/api/platforms/{id}",
        "/api/genres", "/api/genres/{id}",
        "/api/companies", "/api/companies/{id}",
        "/api/features", "/api/features/{id}",
        "/api/categories", "/api/categories/{id}",
        "/api/tags", "/api/tags/{id}",
        "/api/series", "/api/series/{id}",
        "/api/ageratings", "/api/ageratings/{id}",
        "/api/regions", "/api/regions/{id}",
        "/api/sources", "/api/sources/{id}",
        "/api/completionstatuses", "/api/completionstatuses/{id}",
        "/api/emulators", "/api/emulators/{id}",
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
    op = spec["paths"]["/api/games"]["get"]
    params = op.get("parameters", [])
    names = {p["name"] for p in params}
    assert names == EXPECTED_GAMES_PARAMS, (
        f"Missing: {EXPECTED_GAMES_PARAMS - names}, "
        f"Unexpected: {names - EXPECTED_GAMES_PARAMS}"
    )


# ─── Security carve-out ───────────────────────────────────────────────


ANONYMOUS_PATHS = {
    "/api/docs", "/api/openapi.json",
    "/api/swagger-ui.css",
    "/api/swagger-ui-bundle.js",
    "/api/swagger-ui-standalone-preset.js",
    "/api/favicon.png",
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
