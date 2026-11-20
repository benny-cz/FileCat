#include <fcntl.h>
#include <inttypes.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <sys/stat.h>
#include <unistd.h>
#ifdef __APPLE__
#include <sys/mount.h>
#else
#include <sys/vfs.h>
#include <sys/sysmacros.h>
#endif

int main(int argc, char **argv)
{
    if (argc != 2) return 64;
    int fd = open(argv[1], O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (fd < 0) return 65;
    struct statfs space;
    struct stat identity;
    if (fstatfs(fd, &space) || fstat(fd, &identity)) { close(fd); return 66; }
    uint64_t device, unit;
    size_t offset;
#ifdef __APPLE__
    device = (uint32_t)identity.st_dev;
    unit = space.f_bsize;
    offset = offsetof(struct statfs, f_bsize);
#else
    device = ((uint64_t)major(identity.st_dev) << 32) | minor(identity.st_dev);
    unit = space.f_frsize;
    offset = offsetof(struct statfs, f_frsize);
#endif
    printf("{\"pointerBytes\":%zu,\"structureBytes\":%zu,\"unitOffset\":%zu,\"unitsOffset\":%zu,\"device\":\"%016" PRIX64 "\",\"unitBytes\":%" PRIu64 ",\"availableUnits\":%" PRIu64 "}\n",
        sizeof(void *), sizeof(space), offset, offsetof(struct statfs, f_bavail), device, unit, (uint64_t)space.f_bavail);
    return close(fd) == 0 ? 0 : 67;
}
