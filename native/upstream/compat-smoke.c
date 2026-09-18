/* SPDX-License-Identifier: MIT
 * Hardware-free tests of the actual patched upstream headers.
 */
#include "ltfs_fuse.h"
#include <assert.h>

_Static_assert(sizeof(time_t) == 8, "64-bit time required");
_Static_assert(sizeof(fuse_off_t) == 8, "64-bit FUSE offset required");
_Static_assert(sizeof(ltfs_get_thread_id()) == sizeof(uint32_t),
    "Diagnostic thread id must fit upstream trace storage");

static void *check_thread(void *arg)
{
    uint32_t *id = arg;
    *id = ltfs_get_thread_id();
    assert(*id == GetCurrentThreadId());
    return NULL;
}

int main(void)
{
    pthread_t worker;
    uint32_t other_id = 0;
    uint32_t current_id = ltfs_get_thread_id();
    time_t epoch = 0;
    struct tm *utc = gmtime(&epoch);
    struct ltfs_fuse_data data = {0};
    /* Compile-time pointer compatibility check without type-punning casts. */
    struct fuse_statvfs *stats = &data.fs_stats;
    fuse_uid_t *uid = &data.mount_uid;
    fuse_gid_t *gid = &data.mount_gid;
    (void)stats; (void)uid; (void)gid;
    assert(current_id == GetCurrentThreadId());
    assert(utc != NULL && utc->tm_year == 70 && utc->tm_mon == 0 && utc->tm_mday == 1);
    assert(pthread_create(&worker, NULL, check_thread, &other_id) == 0);
    assert(pthread_join(worker, NULL) == 0);
    assert(other_id != 0 && other_id != current_id);
    puts("PASS: CRT time layout, diagnostic thread IDs, WinFsp context field types. No tape access.");
    return 0;
}
