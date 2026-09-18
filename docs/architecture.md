# Architecture

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
