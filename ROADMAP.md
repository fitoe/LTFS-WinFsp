# Roadmap

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
- CLI, tray UI, logs and diagnostics bundle.
- Hardware compatibility matrix and benchmarks.

Write support is intentionally not scheduled until the read-only implementation has extensive hardware coverage.
