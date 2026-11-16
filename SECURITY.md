# Security policy

## Supported version

Security fixes currently target the latest `0.1.x` development release.

## Reporting a vulnerability

Do not publish exploit details in a public issue. Use the repository's private vulnerability-reporting feature when it is enabled, or contact `contact@qa3.me` with a minimal reproduction, affected version, and impact. Do not include private files or personal paths unless they are essential and explicitly redacted.

## Product boundary

Qsearch file review is a local triage aid, not an antivirus engine. It does not execute, delete, quarantine, upload, or automatically remediate reviewed files. Treat all search results and file metadata as untrusted display content; the UI neutralizes bidirectional filename controls but preserves exact stored paths for explicit shell actions.

Development artifacts are unsigned. Verify the published SHA-256 value. A public release should not claim a trusted publisher until both executables are signed by the owner and verified after packaging.
