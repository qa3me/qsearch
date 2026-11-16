# Privacy

Qsearch is designed to run without network access. The runtime contains no HTTP client, socket client, telemetry SDK, analytics package, account system, cloud lookup, update check, advertising identifier, or crash-upload service.

## Local data

Qsearch starts with the current user's personal folders so useful local results can appear without administrator access. Whole-PC fast indexing is a separate opt-in. Only after the user chooses that option does Windows show the elevation prompt; the separate elevated helper then reads file-table and journal metadata from ready fixed NTFS volumes while the main application remains a standard-user process. Selected folders also work without elevation.

Fast NTFS indexing reads names and paths from volume metadata rather than opening every file. On a shared computer, that metadata can include entries under other Windows profiles or locations whose file contents the current user could not normally open. Those entries stay in the local Qsearch index, but they may be visible to anyone using the same Windows account. Use personal/selected-folder indexing instead of whole-PC mode when this local visibility is not appropriate.

Qsearch stores file names, full paths, file/folder type, size, timestamps, attributes, normalized search text, volume checkpoints, user settings, and cached review state in `%LOCALAPPDATA%\Qsearch`. SQLite may create adjacent `qsearch.db-wal` and `qsearch.db-shm` files while the application is running. These records are local search data and are never transmitted.

Local file review can read bounded portions of the selected file, NTFS alternate-stream origin metadata, shortcut metadata, file attributes, and cache-only Authenticode results. Review data is never uploaded.

## What leaves the device

Nothing is sent by Qsearch. File names, paths, search queries, review signals, settings, and usage data are never transmitted. Choosing **Open** can start another application; that application has its own privacy behavior and is outside Qsearch's boundary. The explicit developer, product, and GitHub links open the system browser; any resulting network activity belongs to the browser, not the Qsearch runtime.

## Retention and deletion

Index data remains until the user removes it. Exit Qsearch from the tray, then delete `%LOCALAPPDATA%\Qsearch` to erase the index, consent/settings file, checkpoints, and review cache. Program files are removed by deleting the extracted Qsearch directory.

## Verification

`Qsearch.Architecture.Tests` rejects runtime network-client assembly references, remote endpoint literals, and telemetry packages. The portable verifier runs a packaged offline startup/search smoke. These checks are release gates and are not a substitute for independent review.
