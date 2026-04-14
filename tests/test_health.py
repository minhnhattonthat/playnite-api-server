"""GET /health response shape + auth requirement."""


EXPECTED_COLLECTIONS = {
    "games", "platforms", "companies", "genres", "features",
    "categories", "tags", "series", "ageRatings", "regions",
    "sources", "completionStatuses", "emulators",
}


def test_health_returns_200_with_shape(api, base_url):
    r = api.get(f"{base_url}/api/health")
    assert r.status_code == 200
    body = r.json()
    assert body.get("ok") is True
    assert "version" in body
    assert "counts" in body

    counts = body["counts"]
    missing = EXPECTED_COLLECTIONS - set(counts.keys())
    assert not missing, f"Missing collection keys in counts: {missing}"
    for name, count in counts.items():
        assert isinstance(count, int), f"{name} count is not an int: {count!r}"
        assert count >= 0, f"{name} count is negative: {count}"


def test_health_requires_auth(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/api/health")
    assert r.status_code == 401
    assert r.headers.get("WWW-Authenticate") == "Bearer"
