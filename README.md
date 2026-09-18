# LTFS-WinFsp

A modern, read-only-first LTFS mount for Windows 10/11, backed by WinFsp.

The project exists to replace the legacy FUSE4Win/UMFSDK drive-letter layer used by older vendor LTFS packages. The first goal is safe and fast access to existing LTFS tapes through a normal local Windows drive letter.

> Status: working simulated read-only mount and desktop prototype. Physical tape mounting is not implemented yet. See [test status](docs/TEST-STATUS.md).

## Goals

- Mount an LTFS cartridge as a local drive such as `L:`.
- Keep the first production release strictly read-only.
- Preserve streaming performance for large files.
- Coalesce small/random Windows reads into tape-friendly sequential reads.
- Handle tape removal, device power loss and unmount without hanging processes.
- Provide one small desktop window: select a tape drive and drive letter, mount, unmount, and show status.
- Avoid proprietary FUSE4Win and UMFSDK components.

## Product scope

The product is a simple read-only mounting window. No tray application, background service, automatic mounting, file browser, copy manager, or general-purpose command-line product is planned. Simulator and command-line harnesses are development tools only.

WinFsp provides the Windows filesystem driver and interface; it does not implement LTFS parsing or tape reads. Use the installed official WinFsp runtime, with no need for its optional developer tools at runtime.

## Architecture

```text
Windows applications / Explorer
              |
         WinFsp adapter
              |
 read-only LTFS virtual filesystem
              |
 index cache + sequential read planner
              |
       Windows SCSI backend
              |
          LTO tape drive
```

The core API is independent of WinFsp and the tape transport. This lets us test directory traversal and reads using a simulated tape before hardware tests.

See [docs/architecture.md](docs/architecture.md) and [ROADMAP.md](ROADMAP.md).

## Building

Requirements:

- Windows 10 or Windows 11 x64
- .NET 8 SDK
- WinFsp 2.x (required once the mount adapter lands)

```powershell
dotnet build LTFS.WinFsp.sln
dotnet test LTFS.WinFsp.sln
```

Build the Windows UI using the installed WinFsp runtime:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The solution above contains platform-independent core tests. The build script additionally builds the desktop and mount projects. The current UI deliberately identifies its data as simulated.

## Licensing and clean-room policy

New code in this repository is MIT licensed. Do not copy code from projects that do not provide an explicit license.

HPE LTFS source is available separately under LGPL-2.1. If code derived from it is introduced, it must be isolated, clearly attributed, and distributed under the compatible LGPL terms. HPE binary components such as UMFSDK, FUSE4Win and vendor tape drivers are not part of this repository.

WinFsp remains subject to its own license.

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos.
[WinFsp repository and licensing](https://github.com/winfsp/winfsp).

## Safety

Read-only is a design constraint, not merely a UI option. Write, format, erase, rollback and index-update commands are outside the first release scope.

## Contributing

Issues and focused pull requests are welcome. Hardware reports should include Windows version, HBA, tape drive generation, connection type and observed transfer rate, but never include confidential tape contents.
