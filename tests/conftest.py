"""Shared pytest fixtures for the Playnite API Server integration test suite.

The suite is end-to-end — it runs HTTP requests against a live Playnite
instance with the plugin loaded. See tests/README.md for setup.

Environment variables:
  PLAYNITE_API_BASE   base URL of the server (default http://127.0.0.1:8083)
  PLAYNITE_API_TOKEN  bearer token (required)
"""
import os

import pytest
import requests

from _helpers import TEST_PREFIX


# ─── Configuration fixtures ──────────────────────────────────────────


@pytest.fixture(scope="session")
def base_url():
    return os.environ.get("PLAYNITE_API_BASE", "http://127.0.0.1:8083").rstrip("/")


@pytest.fixture(scope="session")
def token():
    t = os.environ.get("PLAYNITE_API_TOKEN")
    if not t:
        pytest.fail(
            "PLAYNITE_API_TOKEN environment variable is required. "
            "Copy the token from Playnite → Add-ons → Generic → "
            "Playnite API Server → Settings."
        )
    return t


@pytest.fixture(scope="session")
def auth_headers(token):
    return {"Authorization": f"Bearer {token}"}


@pytest.fixture(scope="session")
def api(base_url, auth_headers):
    """Session with Bearer-auth headers preset. Use this for normal tests."""
    s = requests.Session()
    s.headers.update(auth_headers)
    return s


@pytest.fixture(scope="session")
def unauth_session():
    """Session with no Authorization header — for 401/anonymous tests."""
    return requests.Session()


# ─── Session-scoped test-data cleanup ─────────────────────────────────


@pytest.fixture(scope="session", autouse=True)
def cleanup_test_data(api, base_url):
    """Delete any __test__-prefixed games and tags before and after the
    session. Belt-and-suspenders recovery from aborted previous runs —
    if a test crashed between POST and DELETE last time, the orphaned
    row is cleaned here.
    """
    _cleanup(api, base_url)
    yield
    _cleanup(api, base_url)


def _cleanup(api, base_url):
    # Games: paginate so large libraries still get full coverage.
    # __test__ entries sort at the end under OrdinalIgnoreCase ('_' > 'z'),
    # so a small first-page-only sweep would miss them in >1000-game libraries.
    try:
        offset = 0
        page_size = 1000
        while True:
            r = api.get(
                f"{base_url}/api/games",
                params={"limit": page_size, "offset": offset},
            )
            if not r.ok:
                break
            body = r.json()
            items = body.get("items", [])
            if not items:
                break
            for g in items:
                name = g.get("name") or ""
                if name.startswith(TEST_PREFIX):
                    gid = g.get("id")
                    if gid:
                        try:
                            api.delete(f"{base_url}/api/games/{gid}")
                        except Exception:
                            pass
            total = body.get("total", 0)
            offset += len(items)
            if offset >= total:
                break
    except Exception as e:
        print(f"[cleanup] games: {e}")

    # Tags: lookup collections return the full list, no pagination needed.
    try:
        r = api.get(f"{base_url}/api/tags")
        if r.ok:
            for t in r.json():
                name = t.get("name") or ""
                if name.startswith(TEST_PREFIX):
                    tid = t.get("id")
                    if tid:
                        try:
                            api.delete(f"{base_url}/api/tags/{tid}")
                        except Exception:
                            pass
    except Exception as e:
        print(f"[cleanup] tags: {e}")


# ─── Per-test tracked cleanup ─────────────────────────────────────────


@pytest.fixture
def created_games(api, base_url):
    """Per-test list: append the id of any game you create, and it will
    be deleted in teardown. Failures inside the test still trigger cleanup.
    """
    created = []
    yield created
    for gid in created:
        try:
            api.delete(f"{base_url}/api/games/{gid}")
        except Exception:
            pass


@pytest.fixture
def created_tags(api, base_url):
    """Per-test list for tags — same pattern as created_games."""
    created = []
    yield created
    for tid in created:
        try:
            api.delete(f"{base_url}/api/tags/{tid}")
        except Exception:
            pass
