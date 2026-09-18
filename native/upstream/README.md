# HPE native integration workspace

This directory contains **build-enablement work, not a mountable LTFS engine**.

## Licensing

Original audit scripts, configuration and tests: MIT. `patches/*.patch`: LGPL-2.1-only, derived from HPE/IBM/OSR source. Preserve upstream copyright notices. The matching upstream license is included as `COPYING.LIB`. Changes made by LTFS-WinFsp contributors on 2026-09-18 are recorded in the patch. These patches are not covered by the repository's default MIT license.

Base: https://github.com/nix-community/hpe-ltfs/commit/f909474d71deb0e6a7abc71fd47c16a78b9c1eb0

## Reproduce

1. Extract the pinned upstream revision outside the repository. Keep an unmodified copy and a separate working copy. The `ltfs` directory is the patch root.
2. Use an x64 MSYS2 UCRT64 toolchain with GCC, winpthreads, ICU and libxml2. Observed versions are in `toolchain-observed.json`; this is an environment record, **not a complete dependency lock**. Never disable package signature checking.
3. Supply the four WinFsp FUSE headers from revision `5c03dd11ee92bf834ce378c78c0e191e83096298` in a separate directory.
4. From the working copy's `ltfs` directory, run `git apply --check` with the absolute patch path, then `git apply` with that same path. Check success before building.
5. Run from this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File native/upstream/compile-audit.ps1 -SourceDirectory C:\build\hpe-patched\ltfs -ToolchainDirectory C:\build\msys64\ucrt64 -FuseHeaderDirectory C:\build\winfsp-headers -WinFspBuild
powershell -NoProfile -ExecutionPolicy Bypass -File native/upstream/test-compat.ps1 -SourceDirectory C:\build\hpe-patched\ltfs -ToolchainDirectory C:\build\msys64\ucrt64 -FuseHeaderDirectory C:\build\winfsp-headers
```

The audit creates per-source compiler logs, object files and `results.json` under ignored `obj/` (or `-OutputDirectory`). It exits nonzero if **any** source fails; partial progress is not reported as a successful build. Use an unmodified source copy without `-WinFspBuild` to reproduce the baseline. No upstream executable is linked or run by the audit. The separate compatibility test runs only a small CRT/pthread test; it never opens a tape or mounts a drive.

`audit-config/config.h` is a minimal compile-audit configuration, not a substitute for production configuration checks. The x64 time-size assumption is verified by a static assertion. Formatting/repair utilities are intentionally not in this target; their use of the legacy `tm_zone` extension has not been ported.

## Measured result

2026-09-18: baseline **4/39**, patched **36/39** translation units compiled. All 31 core translation units compiled, along with the three scheduler units and two Windows tape driver units. Compatibility smoke test passed (CRT time, diagnostic thread IDs across threads, WinFsp context field types). Patch application check passed against the unmodified revision.

Remaining compile failures:

- `ltfs_fuse.c`: native WinFsp callback/stat types differ from legacy POSIX/modified CRT types.
- `main.c`: UID/GID types need explicit adaptation, followed by startup/lifecycle changes.
- `tape_drivers/windows/ltotape/ltotape_platform.c`: proprietary `FUSE4Win_nonstd.h`; replace the device ownership path, not with fake stubs.

Warnings remain, including legacy pointer-to-integer conversion in filename handling and deprecated ICU normalization APIs. These require review before production. Linking (including message resources), runtime correctness, index durability and performance remain unverified. **36/39 is a compile-audit count, not percentage completion of the product.**

The patch only isolates legacy CRT definitions, fixes diagnostic thread-ID access, restores ICU declarations and adapts the shared FUSE context fields. It does not change tape writing algorithms, bypass failures, activate writes or resolve proprietary mount semantics.
