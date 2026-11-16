# Contributing

Contributions should preserve Qsearch's offline, privacy-first, non-destructive boundary.

1. Open an issue for substantial behavior or architecture changes.
2. Create a focused branch and add a failing test before the implementation change.
3. Run `dotnet format Qsearch.slnx --verify-no-changes`.
4. Run the Release build and all tests from [docs/development.md](docs/development.md).
5. Update English and Persian strings together and keep placeholder parity.
6. Do not add telemetry, network clients, remote endpoints, background downloads, or bundled proprietary fonts.
7. Do not describe review signals as proof that a file is safe or malicious.

Pull requests should state the user-visible change, tests run, privacy/security impact, benchmark impact when relevant, and known limitations. By contributing, you agree that your contribution is licensed under the MIT License.
