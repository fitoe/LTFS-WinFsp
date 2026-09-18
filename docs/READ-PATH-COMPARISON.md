# Read-path comparison and first experiment

Scope: preserve HPE's mounted drive and writes. Use LTFSCopyGUI as a behavioral reference, not as code to copy or a second concurrent device owner. No LTFSCopyGUI code has been imported. Its README describes it as source-available and requests contributor permission for reuse.

## Source observations

HPE references are pinned to `nix-community/hpe-ltfs` revision `f909474d71deb0e6a7abc71fd47c16a78b9c1eb0` (3.4.2). LTFSCopyGUI observations refer to the local source snapshot inspected this session, not a verified match to the executable used for the user's speed test. The installed HPE version is 3.0, not the source version.

| Aspect | HPE source | LTFSCopyGUI source examined | Implication |
| --- | --- | --- | --- |
| Incoming work | `ltfs_fuse_read` forwards requested offset/size to core; `unified_read` reconciles pending writes then reads requested data | Direct `ReadToFileMark` helper loops over tape blocks and saves output; direct block API sends READ(6) | A continuous extraction loop and arbitrary mounted-file requests have different pacing. Measure it. |
| Position tracking | `ltotape_read` increments cached position after success; raw read seeks conditionally; `tape_get_position` is memory-only | `ReadValidationBlock` tracks the next block after success and locates when needed | Both already avoid unnecessary seeks on their sequential path. Do not remove HPE position validation on speculation. |
| Transfer granularity | Core reads using the LTFS label's block size and caches the last block | `TapeUtils.ReadBlock` uses READ(6), with a default 512 KiB limit capped by `GlobalBlockLimit`; some callers pass label-derived limits | Neither proves an arbitrary multi-megabyte SCSI read would be valid or faster. Application size and tape-record size are distinct. |
| Mounted device route | Windows backend has direct DeviceIoControl and proprietary `fuse_media_ioctl` paths | Direct native SCSI path uses DeviceIoControl | Extra latency is a candidate, not a measured cause. Do not switch HPE to a second independent device handle while mounted. |
| Scheduling | Read path must honor pending writes and filesystem requests | Batch extraction/sorting can exploit advance knowledge of selected files | Do not transplant batch-copy assumptions into general filesystem semantics. |

This review does **not** establish that LTFSCopyGUI's speed comes from asynchronous read-ahead, huge buffers, its Rust module, or a specific default block size. Its Rust fast-reader code is not evidence of which tape-read path the tested operation used.

## First experiment without changing vendor binaries

Added [HpeReadProbe](../tools/HpeReadProbe/README.md), an original read-only mounted-file probe. Compare 64/256/1024/8192 KiB application requests over the same prefix on the existing HPE drive. It records bytes, hash, read-call count, time within reads, maximum call latency, opening time and overall loop throughput. No per-request disk logging and no destination-file throughput in the measurement; hash computation is included and explicitly disclosed.

This request-size experiment isolates one application parameter. It is **not** an ablation test of an HPE optimization because no optimization has yet been implemented. Cold/warm caching, tape position and test order remain confounders to record/control. Never present local-file or memory-test throughput as a tape benchmark.

## Follow-on decision

1. If larger requests reproducibly help under comparable conditions, measure how requests reach HPE and whether bounded buffering/request aggregation is appropriate.
2. If larger requests do not help but direct extraction remains fast, instrument the HPE callback/core/backend boundaries to locate wait time. Application-level timing cannot distinguish the layers.
3. Only after locating the bottleneck, test a narrowly scoped read-path change enabled/disabled. Preserve pending-write coherence, EOF, errors, media changes and locks. Do not activate read-ahead against a writable volume without those tests.

No installed components, write algorithms or tape data were changed. Actual 3.0 ABI compatibility and tape measurements remain outstanding.

Sources: [HPE source](https://github.com/nix-community/hpe-ltfs), [LTFSCopyGUI](https://github.com/LCG-Dev-Group/LTFSCopyGUI).
