/* SPDX-License-Identifier: MIT
 * Compile-only interface check. Does not mount or access a tape.
 * These stubs are NOT an LTFS implementation.
 */
#define FUSE_USE_VERSION 26
#include <fuse.h>

typedef char offset_must_be_64_bits[sizeof(fuse_off_t) == 8 ? 1 : -1];

static int probe_read(const char *path, char *buf, size_t size,
    fuse_off_t offset, struct fuse_file_info *info)
{
    (void)path; (void)buf; (void)size; (void)offset; (void)info;
    return -ENOSYS;
}

static int probe_write(const char *path, const char *buf, size_t size,
    fuse_off_t offset, struct fuse_file_info *info)
{
    (void)path; (void)buf; (void)size; (void)offset; (void)info;
    return -ENOSYS;
}

static int probe_flush(const char *path, struct fuse_file_info *info)
{
    (void)path; (void)info;
    return -ENOSYS;
}

static int probe_fsync(const char *path, int datasync, struct fuse_file_info *info)
{
    (void)datasync;
    return probe_flush(path, info);
}

/* Referencing fuse_main also checks its declaration; linking is a separate gate. */
int probe_entry(int argc, char **argv)
{
    struct fuse_operations ops = {0};
    ops.read = probe_read;
    ops.write = probe_write;
    ops.flush = probe_flush;
    ops.fsync = probe_fsync;
    return fuse_main(argc, argv, &ops, 0);
}
