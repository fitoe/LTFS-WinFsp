# Roadmap

## Active priority — preserve HPE mounting, optimize reads

The user has paused WinFsp replacement. Establish compatibility with the installed HPE 3.0 components, measure the existing read path, and make a narrowly scoped read optimization while preserving writes and drive-letter usage. See [audit and deployment gates](docs/HPE-READ-PERFORMANCE.md). No DLL substitution until ABI compatibility and read/write correctness are verified.

The WinFsp integration and prototype plans below are retained as historical work, not current priorities.

## Superseding direction — upstream engine integration

The phases below record the read-only prototype's original roadmap. They are not the current production plan. Do not extend the bespoke reader into a write engine.

1. Pin licensed HPE LTFS source and audit proprietary FUSE hooks (done); compile a WinFsp FUSE callback declaration probe (done).
2. Build the actual upstream core, Windows tape plugin, scheduler and dependencies (36/39 source units now compile, including all core units); link a native worker without FUSE4Win/UMFSDK (pending).
3. Adapt device ownership, ABI types, startup errors and durable unmount; test native failure paths without hardware (pending).
4. Validate real read-only operation and performance on HPE LTO-6 / QLogic FC (pending).
5. Enable upstream writes on disposable test media; verify hashes after remount, index durability, capacity errors and recovery (pending).
6. Distribute a minimal UI: device, drive letter, read-only checkbox, mount/unmount and status. No additional end-user features.

Both reads and writes must use the same engine instance and drive letter. See [integration audit](docs/UPSTREAM-INTEGRATION.md).

## Historical prototype roadmap

## Phase 0 — safe foundation

- Define transport-independent tape and filesystem interfaces.
- Implement an in-memory simulated tape.
- Test path normalization, index traversal, offsets and end-of-file reads.

## Phase 1 — read-only mount prototype

- Implement the WinFsp local-disk adapter.
- Mount the simulator as a local drive.
- Add deterministic mount/unmount and cancellation.

## Phase 2 — LTFS media

- Read and validate LTFS XML indexes.
- Implement Windows SCSI pass-through for supported LTO drives.
- Read file extents from data and index partitions.
- Add index caching without changing the cartridge.

## Phase 3 — performance and resilience

- Sequential prefetch and bounded memory cache.
- Request coalescing and tape-position-aware scheduling.
- Device removal, timeout and power-loss recovery.
- Explorer thumbnail/indexing protections.

## Phase 4 — usable Windows product

- Signed installer and reproducible builds.
- One desktop window: tape drive, drive letter, mount, unmount, and status.
- Hardware compatibility matrix and benchmarks.

Write support is intentionally not scheduled until the read-only implementation has extensive hardware coverage.

Scope decision: no tray, background service, automatic mounting, file browser, copy manager, or extra user tools. Simulation and automated tests remain internal development infrastructure. The initial UI must clearly identify simulated media until a real tape backend is implemented.
