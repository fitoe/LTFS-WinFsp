/* SPDX-License-Identifier: MIT
 * Compile-audit configuration only. Not a production configure result.
 * No executables are linked or run by the audit.
 */
#define PACKAGE_NAME "LTFS-WinFsp-build-audit"
#define PACKAGE_VERSION "3.4.2-audit"
#define LTFS_CONFIG_FILE "ltfs-winfsp-audit.conf"
#include <time.h>
#define SIZEOF_TIME_T 8
_Static_assert(sizeof(time_t) == SIZEOF_TIME_T, "Audit requires 64-bit time_t");
