# Architecture

Qsearch separates contracts, storage, Windows integration, review policy, UI, helper, and evidence tooling.

| Project | Responsibility | Network surface |
|---|---|---|
| `Qsearch.Core` | Search/index contracts, query parsing, text normalization | None |
| `Qsearch.Index` | SQLite schema, transactional batches, FTS search, recovery | None |
| `Qsearch.Windows` | Crawl, watcher/journal primitives, IPC, shell and hotkey integration | Local OS APIs and named pipes only |
| `Qsearch.Safety` | Bounded evidence collection and deterministic review policy | None; Authenticode is cache-only |
| `Qsearch.App` | WPF UI, localization, tray, scopes, compact window | None |
| `Qsearch.ElevatedIndexer` | Optional read-only NTFS helper | Current-user named pipe only |
| `Qsearch.Benchmarks` | Deterministic synthetic performance harness | None |

Search text is normalized at ingestion and query time. Batches and checkpoints commit in one SQLite transaction. Long search terms use FTS5 trigram matching; filters and ranking remain deterministic. A corrupt database is renamed and preserved before rebuild.

Standard folder selection runs without elevation. On first run, the UI discloses the local metadata index and offers machine-wide mode. The helper is a separate executable with `requireAdministrator`; the main application and every other project remain normal-user. Helper IPC uses current-user access, SID/nonce authentication, an allowlist limited to discovered ready fixed NTFS volumes, bounded frames and batches, and an idle timeout. Consent is persisted only after the authenticated helper connects.

The helper streams a complete read-only MFT snapshot in bounded pages, then the coordinator follows the USN journal. A journal identity change or rollover clears and rebuilds only the affected volume scope. Search becomes available after the first committed batch rather than waiting for every volume to finish.

The review engine reads at most a 4 KiB header plus three 64 KiB entropy samples, skips automatic entropy for files over 256 MiB, uses a three-second budget, and returns `Incomplete` when required evidence cannot be obtained. It exposes no delete, quarantine, upload, or launch action.

Architecture tests inspect compiled references, source endpoint literals, project packages, dependency direction, recovery behavior, documentation, and package contracts.
