/* FileCat-owned qualification observer. Build only in a disposable native validation directory, never ship it.
 * Uses the platform C headers/layouts independently of the managed native-buffer reader. No mutation or payload read. */
#define _GNU_SOURCE
#include <sys/stat.h>
#include <sys/types.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdint.h>
#include <inttypes.h>
#include <stdio.h>
#ifdef __linux__
#include <sys/ioctl.h>
#include <linux/fs.h>
#include <sys/sysmacros.h>
#endif

int main(int argc, char **argv)
{
    if (argc != 2) return 64;
    int fd = open(argv[1], O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC);
    if (fd < 0) return 65;
    uint64_t device, inode, links, attributes = 0, mask = 0;
    int64_t size, modified, changed, birth = 0, modified_ns, changed_ns, birth_ns = 0;
    uint32_t owner, group, mode, flags; int has_birth = 0;
#ifdef __linux__
    struct statx s = {0}; long f = 0;
    if (statx(fd, "", AT_EMPTY_PATH, STATX_BASIC_STATS | STATX_BTIME, &s) || !S_ISREG(s.stx_mode) ||
        ioctl(fd, FS_IOC_GETFLAGS, &f)) { close(fd); return 65; }
    device = ((uint64_t)s.stx_dev_major << 32) | s.stx_dev_minor; inode = s.stx_ino; links = s.stx_nlink;
    size = (int64_t)s.stx_size; owner = s.stx_uid; group = s.stx_gid; mode = s.stx_mode & 07777; flags = (uint32_t)f;
    modified = s.stx_mtime.tv_sec; modified_ns = s.stx_mtime.tv_nsec;
    changed = s.stx_ctime.tv_sec; changed_ns = s.stx_ctime.tv_nsec;
    attributes = s.stx_attributes; mask = s.stx_attributes_mask;
    if (s.stx_mask & STATX_BTIME) { has_birth = 1; birth = s.stx_btime.tv_sec; birth_ns = s.stx_btime.tv_nsec; }
#elif defined(__APPLE__)
    struct stat s;
    if (fstat(fd, &s) || !S_ISREG(s.st_mode)) { close(fd); return 65; }
    device = (uint32_t)s.st_dev; inode = s.st_ino; links = s.st_nlink;
    size = s.st_size; owner = s.st_uid; group = s.st_gid; mode = s.st_mode & 07777; flags = s.st_flags;
    modified = s.st_mtimespec.tv_sec; modified_ns = s.st_mtimespec.tv_nsec;
    changed = s.st_ctimespec.tv_sec; changed_ns = s.st_ctimespec.tv_nsec;
    has_birth = 1; birth = s.st_birthtimespec.tv_sec; birth_ns = s.st_birthtimespec.tv_nsec;
#else
    close(fd); return 65;
#endif
    close(fd);
    printf("{\"device\":%" PRIu64 ",\"inode\":%" PRIu64 ",\"links\":%" PRIu64 ",\"size\":%" PRId64
        ",\"owner\":%" PRIu32 ",\"group\":%" PRIu32 ",\"mode\":%" PRIu32 ",\"flags\":%" PRIu32
        ",\"mtime\":%" PRId64 ",\"mtimeNs\":%" PRId64 ",\"ctime\":%" PRId64 ",\"ctimeNs\":%" PRId64
        ",\"hasBirth\":%d,\"birth\":%" PRId64 ",\"birthNs\":%" PRId64 ",\"attributes\":%" PRIu64 ",\"attributeMask\":%" PRIu64 "}\n",
        device,inode,links,size,owner,group,mode,flags,modified,modified_ns,changed,changed_ns,has_birth,birth,birth_ns,attributes,mask);
    return 0;
}
