# Test status and hardware handoff

This is an experimental read-only implementation. The physical tape path is implemented but not hardware-validated.

Implemented:

- Small Windows window with drive selection, read-only mount and unmount.
- WinFsp local mount with deterministic sample files.
- Serialized block reads, single-block cache, and sequential position reuse.
- Bounded XML index parser, explicit logical-to-physical partition mapping.
- Rejection of sparse/overlapping extents, unsupported encoded names, symlinks and Windows name collisions.
- Core fault/cancellation tests and Windows mount smoke test.

Implemented but awaiting hardware validation:

- Hardware validation of the Windows tape API backend (exclusive open, status, position, locate and block reads are implemented but not executed on hardware).
- Reading both LTFS partition labels and validating volume UUID, block size and logical partition mapping.
- Locating terminal indexes in both partitions, checking terminal filemarks, index self-location and generation. Incomplete tails or conflicting indexes fail without repair or fallback.
- Wiring the selected real index into the filesystem adapter.
- Isolated mount process with five-minute startup timeout, cancellation, ten-second graceful stop and a bounded process-termination attempt.

Still pending: hardware throughput, Windows tape-driver block numbering/filemark behavior, media removal/reconnect validation, and optional support for sparse files/encoded names. Fixed-block drivers, non-two-partition tapes and unsupported formats are rejected explicitly. Nothing automatically changes tape modes or repairs media.

Device discovery uses QueryDosDevice without opening the hardware. A listed name does not imply the drive is powered on. Native partition numbers are mapped from verified labels; they are not guessed from LTFS partition letters. The backend uses Windows tape APIs, not raw SCSI commands. Windows requires a read/write-capable handle for tape controls, although this implementation exposes no write operations. Blocking calls run in a separate worker process. Terminating that process does not guarantee a blocked kernel driver releases the device immediately; the UI reports that failure rather than promising recovery.

References for interoperable format and API behavior (no source copied):

- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-settapeposition
- https://github.com/LinearTapeFileSystem/ltfs/blob/master/src/libltfs/xml_writer_libltfs.c
- https://github.com/LinearTapeFileSystem/ltfs/blob/master/src/libltfs/ltfs.c

The XML parser currently accepts a conservative subset. It fails explicitly instead of silently presenting unsupported media incorrectly. The byte-array fixtures and simulator are synthetic, not user tape data.

Build with `powershell -ExecutionPolicy Bypass -File build.ps1`; the script discovers the installed WinFsp assembly. Core tests do not require WinFsp. Run `tests/mount-smoke.ps1` after building the mount project to check directory enumeration, sample data, rejected creation and repeated unmount. The smoke test requires an unused L: drive by default.

Before the first hardware mount: use a physically write-protected tape, ensure other tape applications have released the drive, verify labels/partition mapping and index selection read-only, then compare a known file's checksum. Simulation success is not evidence of hardware throughput or successful cancellation of a blocked kernel request.

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos.
https://github.com/winfsp/winfsp
