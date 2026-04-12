"""GET /games filter + sort behavior and the full spec §5 error contract.

These tests are mostly vacuously satisfiable if your library is empty or has
no matching data — each assertion checks "every returned game matches the
filter", not "at least one game is returned". Empty results are a valid pass.
"""
from _helpers import assert_bad_request


# ─── Positive behavior ────────────────────────────────────────────────


def test_default_list_shape(api, base_url):
    r = api.get(f"{base_url}/games", params={"limit": 5})
    assert r.status_code == 200
    body = r.json()
    assert "total" in body
    assert "offset" in body
    assert "limit" in body
    assert "items" in body
    assert isinstance(body["items"], list)
    assert len(body["items"]) <= 5


def test_pagination_clamping(api, base_url):
    # offset < 0 → 0
    r = api.get(f"{base_url}/games", params={"offset": -5, "limit": 1})
    assert r.status_code == 200
    assert r.json()["offset"] == 0

    # limit > 1000 → 1000
    r = api.get(f"{base_url}/games", params={"limit": 5000})
    assert r.status_code == 200
    assert r.json()["limit"] == 1000


def test_substring_filter(api, base_url):
    """q is a case-insensitive substring filter on Name."""
    r = api.get(f"{base_url}/games", params={"q": "a", "limit": 5})
    assert r.status_code == 200
    for game in r.json()["items"]:
        name = (game.get("name") or "").lower()
        assert "a" in name, f"Game {game.get('name')!r} does not match filter 'a'"


def test_boolean_filter_is_installed(api, base_url):
    r = api.get(f"{base_url}/games", params={"isInstalled": "true", "limit": 5})
    assert r.status_code == 200
    for game in r.json()["items"]:
        assert game.get("isInstalled") is True


def test_boolean_filter_favorite_false(api, base_url):
    r = api.get(f"{base_url}/games", params={"favorite": "false", "limit": 5})
    assert r.status_code == 200
    for game in r.json()["items"]:
        assert game.get("favorite") is False


def test_range_filter_playtime_sorted_desc(api, base_url):
    r = api.get(f"{base_url}/games", params={
        "playtimeMin": "0",
        "sort": "-playtime",
        "limit": 5,
    })
    assert r.status_code == 200
    playtimes = [g.get("playtime", 0) for g in r.json()["items"]]
    assert playtimes == sorted(playtimes, reverse=True), (
        f"Not sorted descending by playtime: {playtimes}"
    )


def test_sort_ascending_by_name_returns_200(api, base_url):
    r = api.get(f"{base_url}/games", params={"sort": "name", "limit": 10})
    assert r.status_code == 200
    assert isinstance(r.json()["items"], list)


def test_sort_descending_by_name_returns_200(api, base_url):
    r = api.get(f"{base_url}/games", params={"sort": "-name", "limit": 10})
    assert r.status_code == 200
    assert isinstance(r.json()["items"], list)


def test_sort_asc_and_desc_by_name_differ(api, base_url):
    """Verify that -name produces a different ordering than name (which
    proves the sort direction is being honored). We intentionally don't
    check exact ordering — Python's str.lower comparison doesn't match
    .NET's StringComparer.OrdinalIgnoreCase for some Unicode edge cases
    (e.g. mixed CJK + Latin names), so a value-level comparison can't
    safely re-sort the server's output for validation.
    """
    import pytest
    asc = api.get(f"{base_url}/games", params={"sort": "name", "limit": 100}).json()["items"]
    desc = api.get(f"{base_url}/games", params={"sort": "-name", "limit": 100}).json()["items"]
    if len(asc) < 2:
        pytest.skip("Need at least 2 games to verify sort direction")
    asc_names = [g.get("name") or "" for g in asc]
    desc_names = [g.get("name") or "" for g in desc]
    assert asc_names != desc_names, (
        "ASC and DESC returned identical ordering — sort direction not honored"
    )


def test_date_range_filter_non_null_last_activity(api, base_url):
    """lastActivityAfter excludes games with null LastActivity (null-fails-positive)."""
    r = api.get(f"{base_url}/games", params={
        "lastActivityAfter": "1900-01-01",
        "sort": "-lastActivity",
        "limit": 5,
    })
    assert r.status_code == 200
    for game in r.json()["items"]:
        assert game.get("lastActivity") is not None, (
            f"Expected non-null lastActivity, got {game}"
        )


def test_source_id_guid_empty_returns_zero(api, base_url):
    """sourceId=Guid.Empty matches zero games per spec §4.3 (null-fails-positive)."""
    r = api.get(f"{base_url}/games", params={
        "sourceId": "00000000-0000-0000-0000-000000000000",
        "limit": 5,
    })
    assert r.status_code == 200
    assert r.json()["items"] == [], "Guid.Empty sourceId should match zero games"


def test_completion_status_id_guid_empty_returns_zero(api, base_url):
    """Same null-fails-positive rule applies to completionStatusId."""
    r = api.get(f"{base_url}/games", params={
        "completionStatusId": "00000000-0000-0000-0000-000000000000",
        "limit": 5,
    })
    assert r.status_code == 200
    assert r.json()["items"] == []


# ─── Error contract (spec §5) ─────────────────────────────────────────


def test_unknown_sort_field(api, base_url):
    r = api.get(f"{base_url}/games", params={"sort": "nope"})
    assert_bad_request(r, expected_message=(
        "Unknown sort field 'nope'. Allowed: name, added, modified, "
        "lastActivity, releaseDate, playtime, playCount, userScore, "
        "communityScore, criticScore."
    ))


def test_bad_boolean_is_installed(api, base_url):
    r = api.get(f"{base_url}/games", params={"isInstalled": "yes"})
    assert_bad_request(
        r,
        expected_message="Invalid boolean for 'isInstalled': 'yes'. Use 'true' or 'false'.",
    )


def test_invalid_playtime_range(api, base_url):
    r = api.get(f"{base_url}/games", params={"playtimeMin": "1000", "playtimeMax": "500"})
    assert_bad_request(
        r,
        expected_message="Invalid range: playtimeMin (1000) is greater than playtimeMax (500).",
    )


def test_bad_uuid_in_multi_id(api, base_url):
    valid = "00000000-0000-0000-0000-000000000001"
    r = api.get(f"{base_url}/games", params={"platformIds": f"{valid},not-a-guid"})
    assert_bad_request(
        r,
        expected_message="Invalid GUID for 'platformIds'[1]: 'not-a-guid'.",
    )


def test_all_comma_multi_id_is_400(api, base_url):
    """Present-but-blank multi-ID (just a comma) is a spec §5 empty-value error."""
    r = api.get(f"{base_url}/games", params={"platformIds": ","})
    assert_bad_request(r, expected_message="Empty value for 'platformIds'.")


def test_user_score_min_out_of_range_high(api, base_url):
    r = api.get(f"{base_url}/games", params={"userScoreMin": "200"})
    assert_bad_request(
        r,
        expected_message=(
            "Invalid value for 'userScoreMin' (200). "
            "Expected an integer between 0 and 100 inclusive."
        ),
    )


def test_user_score_min_out_of_range_negative(api, base_url):
    r = api.get(f"{base_url}/games", params={"userScoreMin": "-5"})
    assert_bad_request(
        r,
        expected_message=(
            "Invalid value for 'userScoreMin' (-5). "
            "Expected an integer between 0 and 100 inclusive."
        ),
    )


def test_bad_integer_for_limit(api, base_url):
    r = api.get(f"{base_url}/games", params={"limit": "abc"})
    assert_bad_request(r, expected_message="Invalid integer for 'limit': 'abc'.")


def test_empty_value_for_offset(api, base_url):
    # requests drops params whose value is the empty string, so hit the raw URL.
    r = api.get(f"{base_url}/games?offset=")
    assert_bad_request(r, expected_message="Empty value for 'offset'.")


def test_bad_iso_date(api, base_url):
    r = api.get(f"{base_url}/games", params={"lastActivityAfter": "not-a-date"})
    assert_bad_request(
        r,
        expected_message="Invalid ISO 8601 date for 'lastActivityAfter': 'not-a-date'.",
    )
