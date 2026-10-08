# Screenshots

No screenshots are committed yet. The customer website (see [frontend](frontend.md)) can now be captured from a throwaway stack. A fake or edited screenshot would misrepresent the project, so add them only from a real run, and review each one before committing.

## Worth capturing

1. **Interactive API reference** (`/scalar/v1` from the Docker stack): the endpoint list with the Authorize button.
2. **Healthy Compose services**: `docker compose ps` showing `postgres` and `api` healthy and `migrate` exited.
3. **Test run**: the final summary of `dotnet test` with all projects passing.
4. **First green CI run**, once it exists on GitHub (see the [publication checklist](publication-checklist.md)).

## Before committing any image

- No local file paths, usernames, machine names, or personal browser tabs or bookmarks.
- No email addresses, tokens, passwords or connection strings. Use a throwaway `.env` and clear sign-in fields.
- Crop to the relevant area; do not include editor windows, notifications or the taskbar.
- Store images under `docs/images/` and reference them from the README with descriptive alt text.
- Keep each image reasonably small (a few hundred KB).
