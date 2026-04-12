"""Games CRUD lifecycle tests — create, read, patch, delete.

Each test creates at least one game with a __test__-prefixed name and
either deletes it manually (for the delete tests) or tracks it in the
created_games fixture for teardown. If Enable write operations is off in
plugin settings, write tests skip with a clear message instead of failing.
"""
import uuid

import pytest

from _helpers import TEST_PREFIX


def _test_name(suffix):
    return f"{TEST_PREFIX}crud-{suffix}-{uuid.uuid4().hex[:8]}"


def _skip_if_writes_disabled(response):
    if response.status_code == 403:
        body = response.json()
        if body.get("error") == "writes_disabled":
            pytest.skip(
                "Writes are disabled in plugin settings. "
                "Enable 'Enable write operations' to run CRUD tests."
            )


def test_create_game(api, base_url, created_games):
    name = _test_name("create")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    assert r.status_code == 201
    game = r.json()
    assert game.get("name") == name
    assert "id" in game
    created_games.append(game["id"])


def test_get_after_create(api, base_url, created_games):
    name = _test_name("get")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    created = r.json()
    created_games.append(created["id"])

    r = api.get(f"{base_url}/games/{created['id']}")
    assert r.status_code == 200
    assert r.json()["name"] == name


def test_patch_favorite(api, base_url, created_games):
    name = _test_name("patch-fav")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    created = r.json()
    created_games.append(created["id"])

    r = api.patch(f"{base_url}/games/{created['id']}", json={"favorite": True})
    assert r.status_code == 200
    assert r.json().get("favorite") is True


def test_patch_multiple_fields(api, base_url, created_games):
    name = _test_name("patch-multi")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    created = r.json()
    created_games.append(created["id"])

    patch = {
        "favorite": True,
        "hidden": False,
        "notes": "integration-test note",
    }
    r = api.patch(f"{base_url}/games/{created['id']}", json=patch)
    assert r.status_code == 200
    updated = r.json()
    assert updated.get("favorite") is True
    assert updated.get("hidden") is False
    assert updated.get("notes") == "integration-test note"


def test_patch_rejects_unknown_field(api, base_url, created_games):
    name = _test_name("patch-unknown")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    created = r.json()
    created_games.append(created["id"])

    r = api.patch(f"{base_url}/games/{created['id']}", json={"bogusField": "nope"})
    assert r.status_code == 400
    body = r.json()
    assert body.get("error") == "bad_request"
    assert "bogusField" in body.get("message", "")


def test_patch_rejects_unknown_foreign_key(api, base_url, created_games):
    """PATCHing a relationship list with a GUID that doesn't exist in the
    target lookup collection returns 409 per ValidateForeignKeys."""
    name = _test_name("patch-fk")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    created = r.json()
    created_games.append(created["id"])

    # 99999999-... is well-formed but unlikely to match any platform.
    fake = "99999999-9999-9999-9999-999999999999"
    r = api.patch(f"{base_url}/games/{created['id']}", json={"platformIds": [fake]})
    assert r.status_code == 409
    body = r.json()
    assert body.get("error") == "conflict"
    assert "platformIds" in body.get("message", "")


def test_delete_game(api, base_url):
    name = _test_name("delete")
    r = api.post(f"{base_url}/games", json={"name": name})
    _skip_if_writes_disabled(r)
    game_id = r.json()["id"]

    r = api.delete(f"{base_url}/games/{game_id}")
    assert r.status_code == 204

    r = api.get(f"{base_url}/games/{game_id}")
    assert r.status_code == 404


def test_create_rejects_missing_name(api, base_url):
    r = api.post(f"{base_url}/games", json={})
    _skip_if_writes_disabled(r)
    assert r.status_code == 400
    assert r.json().get("error") == "bad_request"


def test_create_rejects_whitespace_name(api, base_url):
    r = api.post(f"{base_url}/games", json={"name": "   "})
    _skip_if_writes_disabled(r)
    assert r.status_code == 400


def test_get_rejects_bad_guid(api, base_url):
    r = api.get(f"{base_url}/games/not-a-guid")
    assert r.status_code == 400


def test_get_nonexistent_game_returns_404(api, base_url):
    nonexistent = "11111111-2222-3333-4444-555555555555"
    r = api.get(f"{base_url}/games/{nonexistent}")
    assert r.status_code == 404
