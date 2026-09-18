# HPE mounted-read probe (development tool)

Measures application-level sequential reads through an **existing** mounted file path. No WinFsp dependency, no raw tape handle, no mount/unmount, no driver/configuration changes, no source-file writes. This does not guarantee the underlying vendor mount makes no metadata updates of its own.

Build/test on Windows with .NET 8:

```powershell
dotnet test tests/HpeReadProbe.Tests -c Release
dotnet build tools/HpeReadProbe -c Release
```

When an HPE volume is intentionally mounted and a test file has been selected:

```powershell
tools/HpeReadProbe/bin/Release/net8.0/HpeReadProbe.exe "L:\test-large-file.bin" 64 1024
```

Arguments: existing file, application request size in KiB, maximum prefix length in MiB. The example reads up to 1 GiB from offset zero using 64 KiB requests. It discards data after hashing; it does not copy to disk. JSON is written to stdout only. Repeat **manually** with 256, 1024 and 8192 KiB when desired; this tool never starts multiple readers. Use the same file and prefix limit. Missing/invalid inputs or read errors exit nonzero without emitting a successful report. Ctrl+C is checked between reads; it cannot guarantee interruption of a blocked vendor-driver call.

## What the result means

- `BytesRead` and `Sha256`: consumed prefix, not necessarily the whole file. Compare hashes only when byte counts and file content match. For a direct-extraction comparison, hash the same prefix or select a file smaller than the limit so both hashes cover the whole file.
- `Seconds` / `MiBPerSecond`: read loop including hash processing, excluding opening the file. `OpenSeconds` is separate. First-read positioning can still be included in loop time.
- `ReadCallSeconds` / `MaxReadMilliseconds`: time inside application `Read`, including all layers below it. These are **not** device-command timings.
- `ReadCalls` / `ShortReads`: application calls, not SCSI counts. Short reads are continued, not mistaken for EOF. `ObservedEof` is true only after a zero-byte read, not merely because the size limit was reached.
- Ordinary OS caching is enabled, with a sequential-access hint; .NET's extra file buffer is disabled. Windows/HPE may split, merge or prefetch requests. A larger application buffer does not establish a larger tape record or SCSI transfer.

## Controlled comparison

Do not compare the first cold run with a later cached run and call that a fix. Record request size, order, file size, prefix length, hashes, mount state, initial tape position when known, and whether the run is cold/warm/unknown. Use a sufficiently large file and repeat orders (for example 64/256/1024/8192, then reversed) while recording those conditions. No automatic remount, cache purge or device repositioning is performed.

For LTFSCopyGUI direct extraction, fully unmount HPE first; never let both own the device. Restore HPE only after the direct reader releases it. These are separate benchmark phases, not a proposed user-facing read/write switching workflow. This probe cannot automate or certify ownership transfer.

If request size materially affects **repeatable comparable** results, investigate buffering/request cadence. If all sizes stay slow while direct extraction is fast, measure HPE callback/backend timings next. Neither outcome alone identifies the faulty component.

Validated here: 14 unit cases (limits, hashing, EOF, short reads, failures, cancellation and device-path rejection) plus CLI reads of a local 27,046-byte fixture at four request sizes, all matching the file hash. These tests establish tool behavior only, **not tape performance**.
