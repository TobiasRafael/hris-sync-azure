# Images to capture

The main README references these files. Capture them from a real run (`dotnet run --project src/HrSync.AppHost`,
then the Step 4 scenarios in `requests.http`). Don't use mock-ups.

| File | Status | What to capture |
|---|---|---|
| `trace.png` | done | Dashboard → Traces → the `mockapis: POST /admin/hris/employees` trace from the happy path (4.1), expanded so that `POST api/webhooks/hris`, `publish new-hires`, `process new-hire`, `claim`, `get-employee`, `merge`, `upsert` and the mock calls are all visible. |
| `aspire-dashboard.png` | done | Dashboard → Resources with `storage`, `queues`, `tables`, `mockapis` and `functions` all **Running**, and the URLs column visible (5080 / 7071). |
| `demo.gif` | optional, not referenced yet | About 20–40 s: run 4.1 and 4.3 in `requests.http`, show `/admin/requests` with the `503 (injected)` lines, then the trace. Once added, embed it in the README's "Try it" section (there is a TODO comment there). |

Tips: use a browser width of about 1400 px so the text stays readable when GitHub scales the image down. Crop out the
login token and any local paths, and keep each file under about 1 MB (GIF: 10–15 fps).
