# Integration tests

End-to-end test suite for the Playnite API Server plugin. Runs HTTP requests
against a live Playnite instance with the plugin loaded. There is no unit-test
layer — `IPlayniteAPI.Database` only exists when Playnite is running, so every
assertion is against the real server.

## Setup

Requires Python 3.8+.

```bash
pip install -r tests/requirements.txt
```

This installs `pytest` and `requests` to the active Python environment (use
a venv if you prefer).

## Running

1. **Start Playnite** so the plugin loads and the listener binds. Confirm via
   the settings UI: top-left menu → Add-ons → Generic → Playnite API Server.
2. **Enable "Enable write operations"** in the plugin settings. The CRUD tests
   skip themselves with a clear message if it's off, but you'll get more
   coverage with it on.
3. **Set env vars** and run pytest from the repo root:

   **bash / Git Bash on Windows:**
   ```bash
   export PLAYNITE_API_BASE="http://127.0.0.1:8083"  # default; override if your port differs
   export PLAYNITE_API_TOKEN="<paste from plugin settings>"
   pytest tests/ -v
   ```

   **PowerShell:**
   ```powershell
   $env:PLAYNITE_API_BASE = "http://127.0.0.1:8083"
   $env:PLAYNITE_API_TOKEN = "<paste from plugin settings>"
   pytest tests\ -v
   ```

To run a single file: `pytest tests/test_games_list.py -v`
To run a single test: `pytest tests/test_games_list.py::test_unknown_sort_field -v`

## What it covers

| File | Covers |
|---|---|
| `test_health.py` | `GET /health` response shape (version + all 13 collection counts) + auth requirement |
| `test_auth.py` | Bearer missing/wrong → 401; unknown path → 404 before auth; wrong method on known path → 405 with Allow header |
| `test_docs.py` | `/docs`, `/openapi.json`, and all three Swagger UI asset routes load anonymously with correct content-types and non-stub sizes |
| `test_openapi.py` | Spec is OpenAPI 3.0.3 with the expected 7 schemas, expected paths, and correct security scoping (non-anonymous → bearer, anonymous → empty security array) |
| `test_games_list.py` | Full §5 error contract for all 18 filters + sort + every positive filter behavior + pagination clamping |
| `test_games_crud.py` | `POST → GET → PATCH → DELETE` lifecycle; unknown-field rejection; unknown-foreign-key 409; bad-GUID / not-found handling |
| `test_lookups.py` | Same CRUD lifecycle against `/tags` (representative of all 12 lookup collections — they share one generic controller) |

## Test data and cleanup

Tests that create entries prefix every name with `__test__` and delete their
own data via a teardown fixture. A session-start cleanup pass also deletes
any `__test__`-prefixed games and tags left over from a previous aborted
run, so hard-aborted tests should recover on the next run automatically.

If tests ever leave junk you can inspect, search Playnite's library for
games or tags starting with `__test__` and delete them manually — nothing
else in the project uses that prefix.

## Known limitations

- The CRUD suites need **Enable write operations** on. They skip gracefully
  when it's off, but you'll see the skip markers in pytest output.
- The Swagger UI "Try it out" interactive flow is not tested here — it
  would need a headless browser. The underlying OpenAPI document and route
  registrations are covered structurally by `test_openapi.py`.
- Binary media download (`/games/{id}/media/{kind}`) is not tested because
  it depends on the library having games with covers set.
- Filter tests are allowed to be vacuously true if your library is empty
  or has no matching data — each asserts "every returned game matches the
  filter", not "at least one game is returned". A cold empty library should
  still pass the whole suite.
