# Test status and hardware handoff

This is a simulator prototype, not a hardware-ready LTFS implementation.

Implemented:

- Small Windows window with drive selection, read-only mount and unmount.
- WinFsp local mount with deterministic sample files.
- Serialized block reads, single-block cache, and sequential position reuse.
- Bounded XML index parser, explicit logical-to-physical partition mapping.
- Rejection of sparse/overlapping extents, unsupported encoded names, symlinks and Windows name collisions.
- Core fault/cancellation tests and Windows mount smoke test.

Not implemented yet:

- Hardware validation of the Windows tape API backend (exclusive open, status, position, locate and block reads are implemented but not executed on hardware).
- Reading the LTFS label and finding the latest committed index on tape.
- Wiring a parsed real index into the filesystem adapter.
- Hardware timeouts, offline/reconnect handling, and hardware read verification.

Device discovery uses QueryDosDevice without opening the hardware. A listed name does not imply the drive is powered on. Native partition numbers must be mapped from verified labels; they must not be guessed from LTFS partition letters. The backend uses Windows tape APIs, not raw SCSI commands. Windows requires a read/write-capable handle for tape controls, although this implementation exposes no write operations. Its blocking native calls require process isolation before production UI integration; cancellation currently takes effect before/after an outstanding driver call, not during it.

The XML parser currently accepts a conservative subset. It fails explicitly instead of silently presenting unsupported media incorrectly. The byte-array fixtures and simulator are synthetic, not user tape data.

Build with `powershell -ExecutionPolicy Bypass -File build.ps1`; the script discovers the installed WinFsp assembly. Core tests do not require WinFsp. Run `tests/mount-smoke.ps1` after building the mount project to check directory enumeration, sample data, rejected creation and repeated unmount. The smoke test requires an unused L: drive by default.

Before the first hardware mount: use a physically write-protected tape, ensure other tape applications have released the drive, verify labels/partition mapping and index selection read-only, then compare a known file's checksum. Simulation success is not evidence of hardware throughput or successful cancellation of a blocked kernel request.

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos.
https://github.com/winfsp/winfsp
