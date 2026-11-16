# Threat model

## Assets and trust boundaries

Qsearch protects the user's file metadata, local index, review results, search queries, and explicit scope choices. Trust boundaries exist at filesystem enumeration, NTFS journal reads, the optional elevated helper pipe, shell launch actions, local SQLite storage, and filename rendering.

## In-scope threats

- Crafted names, including bidirectional controls, misleading extensions, reserved names, very long paths, and mixed RTL/LTR text.
- Files changing while they are indexed or reviewed.
- Corrupt or incompatible local index databases.
- Symlink/reparse loops, inaccessible trees, watcher overflow, and NTFS journal rollover.
- Command injection through file paths passed to open/reveal actions.
- A different local user attempting to send activation or helper messages.
- Oversized, malformed, replayed, or idle local IPC messages.
- False confidence caused by presenting heuristic review signals as a malware verdict.

## Controls

- Raw paths are passed as fixed process arguments; they are not concatenated into shell command strings.
- Display-only bidirectional controls are replaced with visible markers while the exact path is retained separately.
- Index writes and checkpoints share SQLite transactions; corrupt databases are preserved before rebuild.
- Review reads have fixed header/sample/size/time bounds and re-check file metadata for change.
- Authenticode uses cache-only retrieval. Review language is evidence-based and non-destructive.
- Helper messages use current-user pipes, nonce/SID handshake, volume allowlists, length and batch caps, and idle exit. Only the helper executable requests elevation.
- The helper opens fixed NTFS volumes read-only. Machine-wide indexing starts only after the disclosure is accepted and the authenticated helper connects; declining remains retryable and does not grant consent.
- Single-instance activation uses a user-scoped mutex and bounded current-user pipe.
- Architecture tests reject network clients, endpoints, and telemetry packages.

## Out of scope

Qsearch does not protect against a compromised administrator/kernel, malicious filesystem drivers, attacks in applications launched by the user, or definitive malware classification. It is not a sandbox, backup system, access-control layer, or substitute for Windows security controls.

## Known gaps for 0.1

Fast mode intentionally excludes non-NTFS, removable, network, offline, and inaccessible volumes. Code signing, an installer, and the full Windows 10/11 device, UAC, accessibility, and scaling matrix remain release-owner tasks.
