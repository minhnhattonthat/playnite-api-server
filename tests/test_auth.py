"""Bearer auth behavior and the anonymous-route dispatch carve-out."""
import requests


def test_missing_token_returns_401(unauth_session, base_url):
    r = unauth_session.get(f"{base_url}/games")
    assert r.status_code == 401
    assert r.headers.get("WWW-Authenticate") == "Bearer"
    body = r.json()
    assert body.get("error") == "unauthorized"


def test_wrong_token_returns_401(base_url):
    r = requests.get(
        f"{base_url}/games",
        headers={"Authorization": "Bearer definitely-not-the-real-token"},
    )
    assert r.status_code == 401
    assert r.headers.get("WWW-Authenticate") == "Bearer"


def test_unknown_path_returns_404_not_401(unauth_session, base_url):
    """Dispatch reorder: unknown paths return 404 before auth is checked."""
    r = unauth_session.get(f"{base_url}/definitely-not-a-real-path")
    assert r.status_code == 404
    body = r.json()
    assert body.get("error") == "not_found"


def test_unknown_path_with_auth_still_404(api, base_url):
    r = api.get(f"{base_url}/another-not-a-real-path")
    assert r.status_code == 404


def test_wrong_method_returns_405(api, base_url):
    """Known path, wrong method, valid auth → 405 with Allow header."""
    r = api.post(f"{base_url}/health")
    assert r.status_code == 405
    allow = r.headers.get("Allow", "")
    assert "GET" in allow
