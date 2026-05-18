# ReplicateFolder

A small C# console application that performs one-way periodic synchronization of a source folder into a replica folder.

After every synchronization pass, the replica folder contents exactly match the source folder:

- files / directories present in source but missing in replica are created;
- files in replica that differ from their source counterpart are overwritten;
- files / directories in replica with no source counterpart are removed;
- symbolic links are replicated as links (their targets are never followed or copied);
- file content equality is checked by size + MD5 (built-in `System.Security.Cryptography.MD5`).

Created / copied / removed operations are logged to both the console and a log file, with timestamps.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build -c Release
```

## Run

```bash
dotnet run --project ReplicateFolder -- \
  --source   ./sample-source   \
  --replica  ./sample-replica  \
  --interval 30                \
  --log      ./sync.log
```

Or, after publishing:

```bash
dotnet publish -c Release -o ./publish
./publish/ReplicateFolder --source ./sample-source --replica ./sample-replica --interval 30 --log ./sync.log
```

Stop with `Ctrl+C` — the app finishes the current iteration and exits cleanly.

## Command-line arguments

| Flag | Description |
| --- | --- |
| `-s`, `--source`   | Source folder. Read only. |
| `-r`, `--replica`  | Replica folder. Modified to mirror source. |
| `-i`, `--interval` | Sync interval, in seconds (positive integer). |
| `-l`, `--log`      | Path to log file. Created if missing, appended otherwise. |
| `--once`           | Run a single sync pass and exit (handy for testing). |
| `-h`, `--help`     | Show usage. |

The source and replica paths must not be the same or nested inside one another.

## How it works

Each iteration runs two passes over the directory trees:

1. **Replicate.** Walk the source tree and ensure every directory, file, and symbolic link exists in the replica with matching content / target.
   - File equality is checked by size first (cheap), then by MD5 hash (authoritative).
   - Symbolic links are detected via `FileSystemInfo.LinkTarget` and re-created with `File.CreateSymbolicLink` / `Directory.CreateSymbolicLink`; the walker never descends into a symlinked directory.
2. **Prune.** Walk the replica tree and delete any file, directory, or link that no longer has a counterpart in the source. Directory symlinks are unlinked without touching their targets.

Errors on individual files/directories are logged but do not abort the iteration — the next sync pass will retry.

## Symlinks on Windows

`CreateSymbolicLink` on Windows needs **one** of the following:

- **Developer Mode** enabled (Windows 10/11), or
- The **`SeCreateSymbolicLinkPrivilege`** (typically only admins have it), or
- The process running **elevated** (Run as administrator).

Without one of these, creating a symlink throws `UnauthorizedAccessException`. The error is logged and the sync continues; that entry simply won't be mirrored until privileges are granted. On Linux and macOS no special privileges are required.

## Project layout

```
.
├── ReplicateFolder/
│   ├── ReplicateFolder.csproj   # net10.0 console app
│   ├── Program.cs               # entry point + main loop
│   ├── CommandLineOptions.cs    # CLI arg definitions
│   ├── Logger.cs                # thread-safe console + file logger
│   └── Synchronizer.cs          # the actual sync logic
└── ReplicateFolder.Tests/       # xUnit test project (net10.0)
    ├── TempWorkspace.cs                  # per-test temp source/replica fixture
    ├── SynchronizerTests.cs              # core unit tests
    ├── SynchronizerEdgeCaseTests.cs      # special chars, unicode, long names, …
    └── SynchronizerPerformanceTests.cs   # opt-in benchmark tests
```

## Tests

The solution ships with an xUnit test project (`ReplicateFolder.Tests`).

### Run the regular (fast) test suite

```bash
dotnet test
```

Currently 37 tests covering:

- **Core sync behavior** — copy new file, recreate nested trees, update on
  content change (incl. same-size different bytes), no rewrite when content
  matches, remove deleted files/directories, prune extras added to the
  replica, type-mismatch resolution in both directions (dir↔file), auto-create
  missing replica root, empty source clears replica, zero-byte files,
  preserved empty directories, missing source logs an error without throwing,
  cancellation token is honored.
- **Edge cases**
  - Filenames with special characters (spaces, dots, parentheses, brackets,
    `+`, `=`, `&`, `#`, `%`, hidden-style `.x`, trailing dot).
  - Unicode filenames (diacritics, Cyrillic, Japanese, emoji).
  - Long filenames (200 chars, near the path limit).
  - Deeply nested directory trees (20 levels).
  - Many files in a single directory (500).
  - Binary files containing every byte value 0..255.
  - Case-only filename change on case-insensitive filesystems.
  - Idempotency: snapshotting the replica after 1 vs 3 syncs must match.

Each test runs in its own isolated `%TEMP%\ReplicateFolderTests_<guid>\`
workspace, which is deleted on test teardown.

### Run the performance / stress tests

These are **skipped by default** because they need several GB of free disk
space and noticeable wall-clock time. Opt in with an environment variable:

PowerShell:
```powershell
$env:REPLICATEFOLDER_RUN_PERF = "1"
dotnet test
```

bash / zsh:
```bash
REPLICATEFOLDER_RUN_PERF=1 dotnet test
```

Two scenarios are exercised; each prints its measurements via
`ITestOutputHelper`:

1. **`Replicates_single_3GB_file`** — creates a 3 GiB file in the source,
   replicates it, asserts the destination size, then measures a second
   no-op sync (which still has to re-hash both sides). A pre-flight
   `DriveInfo` check guards against insufficient free space.
2. **`Replicates_10000_small_files`** — creates 10 000 small files spread
   across 100 subdirectories, measures initial throughput (files/sec) and
   re-sync time when everything is already up to date.

### Other test categories to consider

Useful additions depending on the target environment:

- **Symlink handling.** The synchronizer has explicit logic for file and
  directory symlinks; tests for it would need to be gated similarly to perf
  tests because creating symlinks on Windows requires Developer Mode or
  admin (see [Symlinks on Windows](#symlinks-on-windows)).
- **Locked / in-use source files** — open a file with `FileShare.None` and
  assert the error is logged and sibling entries are still processed.
- **Read-only / ACL-denied entries** — verify graceful error logging.
- **Mid-sync mutation** — modify or delete a source file while `SyncOnce` is
  iterating, to confirm race-condition resilience.
- **CLI tests** for `CommandLineOptions` / `Program` (argument parsing, exit
  codes, interval scheduling).
- **Logger tests** — concurrent writers, formatting, dispose semantics,
  append-on-restart.
- **Cross-platform path quirks** — Linux case-sensitive collisions, names
  containing `\` or newlines.
- **Reserved Windows names** (`CON`, `PRN`, `AUX`, `NUL`, `COM1`…) — only
  interesting if you want defensive behavior; usually you just want a clean
  error.
- **Very large directory fan-out** (100 000+ files) as a stress test for
  enumeration memory use.


## Notes on the task

- No third-party folder-synchronization libraries are used — only `System.IO` for filesystem access and the built-in `System.Security.Cryptography.MD5` for hashing, as the task explicitly permits.
- [CommandLineParser](https://www.nuget.org/packages/CommandLineParser) is used for argument parsing.

