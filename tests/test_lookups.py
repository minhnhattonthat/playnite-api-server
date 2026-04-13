"""Lookup collection CRUD — exercised against /tags as representative of
all 12 collections (platforms, genres, companies, features, categories,
tags, series, ageratings, regions, sources, completionstatuses, emulators).
All 12 share the same `LookupController<T>` generic implementation, so
coverage of one is effectively coverage of all."""
import uuid

import pytest

from _helpers import TEST_PREFIX


def _tag_name(suffix):
    return f"{TEST_PREFIX}tag-{suffix}-{uuid.uuid4().hex[:8]}"


def _skip_if_writes_disabled(response):
    """Skip when the configured token lacks the write scope."""
    if response.status_code == 403:
        body = response.json()
        if body.get("error") == "forbidden" and "scope" in body.get("message", "").lower():
            pytest.skip(
                "Configured token lacks the write scope. "
                "Use a token with read+write to run CRUD tests."
            )


def test_list_tags(api, base_url):
    r = api.get(f"{base_url}/tags")
    assert r.status_code == 200
    items = r.json()
    assert isinstance(items, list)
    for tag in items:
        assert "id" in tag
        assert "name" in tag


def test_create_tag(api, base_url, created_tags):
    name = _tag_name("create")
    r = api.post(f"{base_url}/tags", json={"name": name})
    _skip_if_writes_disabled(r)
    assert r.status_code == 201
    body = r.json()
    assert body.get("name") == name
    assert "id" in body
    created_tags.append(body["id"])


def test_get_tag_after_create(api, base_url, created_tags):
    name = _tag_name("get")
    r = api.post(f"{base_url}/tags", json={"name": name})
    _skip_if_writes_disabled(r)
    tid = r.json()["id"]
    created_tags.append(tid)

    r = api.get(f"{base_url}/tags/{tid}")
    assert r.status_code == 200
    assert r.json()["name"] == name


def test_patch_tag_name(api, base_url, created_tags):
    original = _tag_name("rename-from")
    r = api.post(f"{base_url}/tags", json={"name": original})
    _skip_if_writes_disabled(r)
    tid = r.json()["id"]
    created_tags.append(tid)

    new_name = _tag_name("rename-to")
    r = api.patch(f"{base_url}/tags/{tid}", json={"name": new_name})
    assert r.status_code == 200
    assert r.json()["name"] == new_name


def test_patch_rejects_unknown_field(api, base_url, created_tags):
    name = _tag_name("patch-unknown")
    r = api.post(f"{base_url}/tags", json={"name": name})
    _skip_if_writes_disabled(r)
    tid = r.json()["id"]
    created_tags.append(tid)

    r = api.patch(f"{base_url}/tags/{tid}", json={"notAName": "nope"})
    assert r.status_code == 400
    assert r.json().get("error") == "bad_request"


def test_delete_tag(api, base_url):
    name = _tag_name("delete")
    r = api.post(f"{base_url}/tags", json={"name": name})
    _skip_if_writes_disabled(r)
    tid = r.json()["id"]

    r = api.delete(f"{base_url}/tags/{tid}")
    assert r.status_code == 204

    r = api.get(f"{base_url}/tags/{tid}")
    assert r.status_code == 404


def test_create_rejects_empty_name(api, base_url):
    r = api.post(f"{base_url}/tags", json={"name": ""})
    _skip_if_writes_disabled(r)
    assert r.status_code == 400


def test_get_rejects_bad_guid(api, base_url):
    r = api.get(f"{base_url}/tags/not-a-guid")
    assert r.status_code == 400


def test_get_nonexistent_tag_returns_404(api, base_url):
    nonexistent = "11111111-2222-3333-4444-555555555555"
    r = api.get(f"{base_url}/tags/{nonexistent}")
    assert r.status_code == 404
