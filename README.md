# LTFS-WinFsp

> **Current direction (2026-09-18): retain the installed HPE mount layer and optimize its read path. WinFsp replacement is paused at the user's request.** See [HPE read-performance audit](docs/HPE-READ-PERFORMANCE.md). Installed HPE 3.0 and audited 3.4.2 source are not a verified binary-compatible pair; no installed component has been replaced. The WinFsp design below is historical/experimental, not the active plan.

A minimal LTFS drive-letter application for Windows 10/11, targeting unified reads and writes using an existing open-source LTFS engine and WinFsp.

The project exists to replace the legacy FUSE4Win/UMFSDK drive-letter layer used by older vendor LTFS packages. The first goal is safe and fast access to existing LTFS tapes through a normal local Windows drive letter.

> Direction update: reuse HPE LTFS core, write operations and scheduler through WinFsp. Do not implement a second write engine. Preview5 and the implementation described below remain an experimental read-only baseline, not the final architecture. Native integration and hardware validation are incomplete. See [integration plan](docs/UPSTREAM-INTEGRATION.md) and [prototype test status](docs/TEST-STATUS.md).

## Goals

- Mount an LTFS cartridge as a local drive such as `L:`.
- Reuse upstream reads and writes in one engine; validate read-only access before enabling writes on disposable test media.
- Preserve streaming performance for large files.
- Coalesce small/random Windows reads into tape-friendly sequential reads.
- Handle tape removal, device power loss and unmount without hanging processes.
- Provide one small desktop window: select a tape drive and drive letter, mount, unmount, and show status.
- Avoid proprietary FUSE4Win and UMFSDK components.

## Product scope

The product is a simple mounting window: device, drive letter, read-only checkbox, mount/unmount and status. Reads and writes share one drive letter and engine instance. No tray application, background service, automatic mounting, file browser, copy manager, or general-purpose command-line product is planned. Simulator and command-line harnesses are development tools only.

WinFsp provides the Windows filesystem driver and interface; it does not implement LTFS parsing or tape reads. Use the installed official WinFsp runtime, with no need for its optional developer tools at runtime.

## Architecture

```text
Windows applications / Explorer
              |
         WinFsp adapter
              |
 HPE LTFS FUSE operations + core
              |
 upstream scheduler + index handling
              |
       Windows SCSI backend
              |
          LTO tape drive
```

This is the target architecture. The existing .NET parser/reader and simulator remain an experimental read-only baseline, not the final read/write engine. Keep the UI and process isolation, but adapt shutdown for upstream index synchronization and explicit commit-error reporting.

See [docs/architecture.md](docs/architecture.md) and [ROADMAP.md](ROADMAP.md).

## Building

Requirements:

- Windows 10 or Windows 11 x64
- .NET 8 SDK
- WinFsp 2.x

```powershell
dotnet build LTFS.WinFsp.sln
dotnet test LTFS.WinFsp.sln
```

Build the Windows UI using the installed WinFsp runtime:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The solution above contains platform-independent core tests. The build script additionally tests Windows discovery/metadata loading and builds the desktop and mount projects. The UI distinguishes simulated and real tape devices. Mounting runs in a separate process with cancellation and bounded shutdown waits.

## Licensing and clean-room policy

Original code in this repository is MIT licensed. Upstream-derived patches under `native/upstream/patches` are LGPL-2.1-only; see that directory's notices and `COPYING.LIB`. Do not copy code from projects that do not provide an explicit license.

HPE LTFS source is available separately under LGPL-2.1. If code derived from it is introduced, it must be isolated, clearly attributed, and distributed under the compatible LGPL terms. HPE binary components such as UMFSDK, FUSE4Win and vendor tape drivers are not part of this repository.

WinFsp remains subject to its own license.

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos.
[WinFsp repository and licensing](https://github.com/winfsp/winfsp).

## Safety

Preview5 remains read-only. Future writes must use upstream LTFS semantics and pass disposable-media durability tests before release. Format, erase and rollback tools are outside the product scope. A read-only checkbox alone does not prove that engine startup/shutdown makes no media modifications.

## Contributing

Issues and focused pull requests are welcome. Hardware reports should include Windows version, HBA, tape drive generation, connection type and observed transfer rate, but never include confidential tape contents.
