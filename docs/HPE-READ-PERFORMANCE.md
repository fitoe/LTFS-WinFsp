# Active direction: improve HPE reads without replacing the mount layer

Decision (2026-09-18): keep the existing HPE drive-letter experience and write capability. Pause WinFsp integration. Investigate and optimize the open-source HPE read path, subject to compatibility and measurement. Repository naming and earlier prototypes remain unchanged; they are not the current implementation target.

## Installed components: observed, not assumed

Static inspection found LTFS for Windows **3.0.0**, with `LTFSConfigurator.exe` version 3.0.0.0 and a `3.0.0` string in `libltfs.dll`. The available audited source is HPE **3.4.2**. This is not a verified matching source/binary pair.

- `libltfs.dll` imports the old MSVCRT, `pthreadGC2-w64.dll`, ICU50 and libxml2.
- `libiosched-unified.dll` imports `libltfs.dll`, including `ltfs_fsraw_read`, and exports `iosched_get_ops`.
- Active `ltfs.conf` selects `unified` for scheduling and `ltotape_win` for the device backend.
- `libdriver-ltotape-win.dll` imports `FUSE4Win.dll`, including `fuse_get_media_device` and `fuse_media_ioctl`.
- `FUSE4Win.dll` imports the UMFSDK components. Existing mount integration is retained, not replaced.
- FUSE4WinSvc was stopped during this inspection. No service was started and no device was opened. Installed files are not proof of a currently working mounted session.

The previously compiled UCRT64/modern-winpthreads/ICU78 objects are **not drop-in replacements** for this installation. Matching function names do not prove compatibility: structures, callback tables, offsets, locks, allocation ownership and CRT behavior must also match. The previous 36/39 compile result belongs to the paused WinFsp branch and must not be presented as progress toward binary-compatible 3.0 replacements.

## Read path in available 3.4.2 source

`ltfs_fuse_read` -> `ltfs_fsops_read` -> scheduler -> `ltfs_fsraw_read` -> `tape_read` -> Windows backend.

- `src/iosched/unified.c:495`: `unified_read` handles overlaps with pending writes. With no pending writes, it forwards the caller's requested size to `ltfs_fsraw_read`. This path is not an asynchronous sequential read-ahead queue.
- `src/libltfs/ltfs_fsops_raw.c:545`: a last-block cache is allocated using the tape's label block size. It can reuse the previous block and only seeks when the cached position requires it.
- `src/libltfs/tape.c:1087`: `tape_get_position` only copies cached state. Do **not** treat every invocation as a physical READ POSITION command or remove it on that assumption.
- Mounted device communication can traverse `fuse_media_ioctl`, even after the request has reached the open-source backend. Mount success does not exclude latency in this path.

These are code observations, not measured causes of the user's slow reads. Do not assume the installed 3.0 implementation is identical.

## Narrowest candidate intervention

The configurable scheduler DLL is a potential extension point, not yet an approved replacement boundary. Prefer measuring/optimizing reads there if the exact 3.0 callback ABI and runtime can be established. Otherwise investigate a matching core rebuild; do not deploy guessed structures, change the active scheduler or replace installed DLLs as an experiment.

Preserve existing write ordering and read-after-write semantics. Any future read-ahead cache needs invalidation for writes, truncation, media changes and recovery, plus bounded memory and cancellation. A request for N bytes must still return no more than N bytes. Do not simply enlarge the caller's buffer or change media block size. Avoid a second independent device owner.

## Next gates

1. Obtain matching 3.0 source/dependency headers or otherwise establish the exact ABI. The additional source mirrors inspected currently identify as 3.5.0 (`leavelet/ltfs-hp`) and 3.6.0 (`mikee47/HPE-LTFS`); neither establishes compatibility with 3.0.
2. Build an isolated diagnostic variant that preserves the original mount interface, without installing it. Record request size/offset, callback duration, backend READ and LOCATE counts/duration, and inter-request gaps; aggregate rather than synchronously log every block.
3. When hardware is on, compare the same large file on the original HPE drive letter and direct extraction, with consistent cache/position conditions and hashes. Separately identify application gaps, engine overhead and device-command latency.
4. Add only the optimization supported by measurements; compare enabled/disabled behavior and run read/write consistency regression tests before considering deployment.

No speed improvement is claimed yet. No installed file, registry value, service setting, tape data or LTFS index was changed during this audit.

## Reproducible static inventory

`native/vendor-audit/inspect-installation.ps1` records versions, SHA256 fingerprints and static PE imports/exports without loading vendor code. Supply a trusted `objdump.exe` and an output directory outside the repository. Reports contain local paths; do not publish them without review. Vendor binaries are never copied into this repository.
