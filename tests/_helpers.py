"""Non-fixture helpers for the integration test suite.

Named with a leading underscore so pytest doesn't try to collect it as
a test module.
"""


# Prefix every test-created entity name with this. The session-level
# cleanup fixture in conftest.py uses this prefix to find and delete
# leftover rows from aborted previous runs.
TEST_PREFIX = "__test__"


def assert_bad_request(response, expected_message=None):
    """Assert a response is a 400 with the standard {error, message} body.

    If expected_message is provided, also assert the message matches
    literally — useful for regressing the spec §5 error contract.
    """
    assert response.status_code == 400, (
        f"Expected 400, got {response.status_code}. "
        f"URL: {response.url}. Body: {response.text[:500]}"
    )
    body = response.json()
    assert body.get("error") == "bad_request", (
        f"Expected error='bad_request', got {body!r}"
    )
    if expected_message is not None:
        assert body.get("message") == expected_message, (
            f"Expected message:\n  {expected_message!r}\n"
            f"Got:\n  {body.get('message')!r}"
        )
