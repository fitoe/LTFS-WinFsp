# Architecture

## Current target

Windows applications -> WinFsp FUSE adapter -> HPE LTFS operations/core/scheduler -> upstream Windows tape backend -> LTO drive.

One native worker owns one tape session for both reading and writing. Our code handles Windows integration, readiness/error reporting and a minimal UI; upstream owns tape layout, allocation, writes and index commits. No split read/write engines.

Retain existing UI/process isolation, but do not reuse the read-only prototype's forced-kill timeout as successful writable unmount. A failed commit must remain visible; process exit is not proof of durable data. Audit startup/shutdown and MAM/index changes before promising no media modifications in read-only mode.

The .NET parser, reader and simulator remain experimental infrastructure. Their tests do not validate the upstream engine or hardware. See [integration audit](UPSTREAM-INTEGRATION.md).

## Historical read-only prototype

LTFS-WinFsp separates Windows filesystem behavior from tape mechanics.

## Components

1. **Virtual filesystem** — immutable directory tree, metadata and file extents.
2. **Read planner** — turns arbitrary Windows reads into ordered tape reads and bounded prefetch operations.
3. **Tape transport** — device discovery, partition selection, locate and read operations.
4. **WinFsp adapter** — exposes the immutable tree as a local Windows disk.
5. **Host** — owns mount state, cancellation, diagnostics and clean shutdown.

## Safety boundary

The public tape interface contains no write, erase, format or mode-changing operation. A read-only build therefore cannot accidentally expose mutation through the filesystem adapter.

## Initial compatibility target

- Windows 10/11 x64
- WinFsp 2.x
- Standalone LTO-5 through LTO-8 drives
- LTFS format generations readable by HPE StoreOpen 3.x

Initial hardware validation will use an HPE LTO-6 drive connected through a QLogic Fibre Channel adapter.
