# Qsearch benchmark methodology

The benchmark is a deterministic, offline harness for comparing Qsearch builds on the same machine. It is not a comparison with another product and is not a universal performance claim.

The published full workload uses:

- seed `0x5133534541524348`;
- 1,000,000 synthetic file records;
- 30 warm-up searches;
- 500 measured searches split equally across prefix, contained term, exact name, extension filter, and exclusion classes;
- a maximum of 100 returned rows per query.

The same run also records startup-to-first-searchable-batch, all ten empty-query sort/direction combinations, ten simulated journal catch-up batches, peak process working set, and SQLite database/WAL/SHM bytes. These phase timings are measurements of the deterministic harness, not promises about every physical disk or Windows installation.

Run the fast validation workload:

```powershell
dotnet run --project src/Qsearch.Benchmarks/Qsearch.Benchmarks.csproj -c Release -- --smoke --output work/benchmarks/smoke.json
```

Run release evidence explicitly:

```powershell
dotnet run --project src/Qsearch.Benchmarks/Qsearch.Benchmarks.csproj -c Release -- --records 1000000 --queries 500 --seed 5133534541524348 --output work/benchmarks/release.json
```

Reports include OS/runtime/architecture, corpus size and seed, index bytes, peak process working set, startup-to-searchable time, empty-sort and journal-catch-up samples, returned rows, raw query samples, and p50/p95/p99 query latency. Keep raw JSON with release notes so every number remains attributable to one machine, build, corpus, and seed.
