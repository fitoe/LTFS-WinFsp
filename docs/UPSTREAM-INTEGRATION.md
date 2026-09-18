# Upstream integration audit — 2026-09-18

## Pinned sources

- HPE StoreOpen 3.4.2 mirror: https://github.com/nix-community/hpe-ltfs
- Revision: `f909474d71deb0e6a7abc71fd47c16a78b9c1eb0`, importing `HPE_StoreOpen_Software_3.4.2_Source_Z7550-02501.tar.gz`. This does not establish identity with the installed vendor version.
- License: upstream `COPYING.LIB`, LGPL-2.1. Isolate and attribute derived patches; preserve applicable source/relinking obligations. Project MIT licensing does not relicense upstream.
- WinFsp headers: https://github.com/winfsp/winfsp/tree/5c03dd11ee92bf834ce378c78c0e191e83096298/inc/fuse (v2.0). WinFsp licensing and FLOSS exception remain applicable.
- No proprietary FUSE4Win/UMFSDK binaries or source included.

## Audit findings

| Upstream location | Finding and required adaptation |
| --- | --- |
| `ltfs/src/libltfs/ltfs_fuse_version.h` | FUSE API 26; use WinFsp FUSE2, but API version alone does not prove ABI compatibility. |
| `ltfs/src/ltfs_fuse.c`, operations table | Existing write, flush, fsync, create and metadata operations: reuse them, not a custom write queue. |
| `ltfs/src/tape_drivers/windows/ltotape/ltotape_platform.c:199,368` | Proprietary `fuse_media_ioctl` / `fuse_get_media_device`; alternate CreateFile/DeviceIoControl path exists. Explicitly own a valid handle; audit sharing, locking and every cleanup path. Do not hide invalid handles with dummy shims. |
| Same file, `ltotape_close` / `ltotape_close_raw` | Rewind, dismount and cleanup differ. Review ownership and legacy volume-control calls before reuse. |
| `ltfs/src/main.c`, `single_drive_main`; `ltfs_fuse.c`, init | HPE defers media preparation to FUSE init. Move fallible preparation before publishing the drive and report explicit errors. |
| `ltfs_fuse.c:1304` and subsequent branches | Uses `conn->reserved[0]` for errors and returns NULL. Replace this proprietary channel; NULL init userdata is not a portable mount-failure signal. |
| `ltfs/src/libltfs/arch/win/win_util.h` | Macro-dependent dummy FUSE calls and Windows definitions. Add a WinFsp-specific build path, rather than simply deleting `HPE_mingw_BUILD`. |
| `ltfs_fuse.c`, `ltfs_fuse_umount` | Stops periodic sync and calls `ltfs_unmount(SYNC_UNMOUNT, ...)`. Preserve commit ordering and report its outcome before claiming safe unmount. |

WinFsp native headers use `fuse_off_t`, `struct fuse_stat` and other explicit types. Convert at the FUSE boundary, not with unchecked casts/global replacements. Audit option timing: adding `-oro` during init may be too late in another FUSE implementation.

## Reproducible compile-only probe

`native/probe/fuse26-probe.c` is original scaffolding, not copied HPE code or an LTFS implementation. It checks read/write/flush/fsync declarations, `fuse_main` visibility and 64-bit offsets. Stubs reject operations.

Supply `fuse.h`, `fuse_common.h`, `fuse_opt.h`, `winfsp_fuse.h` from the pinned revision in an external directory, then run from the repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File native/probe/build.ps1 -HeaderDirectory C:\path\to\headers
```

Result here: PASS with MSVC 14.44, x64, C11, `/W4 /WX`, WinFsp v2.0 headers; object file generated. This proves only the adapter-facing declarations compile. It does **not** compile the HPE callbacks or prove linking, mounting, compatibility, durability or speed. No device was opened.

## Actual upstream compile audit

An isolated MSYS2 UCRT64 toolchain is now available in the external work directory. Actual HPE sources were compiled with GCC, ICU and libxml2: the unmodified baseline compiled 4 of 39 translation units; the scoped compatibility patch compiled 36 of 39, including all 31 core units. Hardware-free CRT/thread/context compatibility tests passed. See [reproduction, license and remaining failures](../native/upstream/README.md).

The remaining three source failures are the FUSE callbacks, main entry point and proprietary device-access path. No engine executable has been linked, mounted or tested against hardware. This compile count is not a product-completion percentage.

## Historical initial build assessment

HPE's historical Windows build expects MinGW, pthreads, libxml2, UUID and ICU (old instructions reference ICU50). This machine has MSVC, but no discovered MinGW toolchain or installed WinFsp SDK headers. Headers were fetched into external work storage for this probe. The full engine has not been compiled; exact compiler/dependency failures have not yet been measured.

Next: finish callback and device-path adaptation, build message resources, link and test the worker, then pin the complete transitive dependency set for reproducibility. Do not enable UI writes or ship a dummy engine before these gates pass.

## Verification gates

Hardware-free native tests: missing media, invalid index, failed open, cancellation, handle cleanup and failed commits with a mocked backend. Existing .NET simulator tests do not substitute for these.

First hardware target: Windows 11 x64, HPE LTO-6 / QLogic FC. Read-only testing first; writes and interruption tests require explicitly designated disposable media. Verify remount hashes, capacity and write-protect errors, recovery and cross-reading with a compatible LTFS implementation. No formatting or erasing as part of integration.

Keep UI limited to device, drive letter, read-only, mount/unmount and status. Writable unmount must wait for upstream synchronization and surface failures. Never inherit the prototype's forced-kill policy as a successful durable unmount.
