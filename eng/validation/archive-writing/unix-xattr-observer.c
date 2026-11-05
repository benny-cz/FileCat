#include <fcntl.h>
#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/xattr.h>
#include <unistd.h>
#ifndef __APPLE__
#include <sys/sysmacros.h>
#endif

static ssize_t names(int fd, char *bytes, size_t length)
{
#ifdef __APPLE__
    return flistxattr(fd, bytes, length, 0);
#else
    return flistxattr(fd, bytes, length);
#endif
}
static ssize_t value(int fd, const char *name, void *bytes, size_t length)
{
#ifdef __APPLE__
    return fgetxattr(fd, name, bytes, length, 0, 0);
#else
    return fgetxattr(fd, name, bytes, length);
#endif
}
static void hex(const unsigned char *bytes, size_t length)
{
    static const char digits[] = "0123456789ABCDEF";
    for (size_t i = 0; i < length; i++) { putchar(digits[bytes[i] >> 4]); putchar(digits[bytes[i] & 15]); }
}
int main(int argc, char **argv)
{
    if (argc != 2) return 64;
    int fd = open(argv[1], O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC);
    if (fd < 0) return 65;
    struct stat identity;
    if (fstat(fd, &identity) || !S_ISREG(identity.st_mode)) { close(fd); return 66; }
    ssize_t length = names(fd, NULL, 0);
    if (length < 0 || length > 65536) { close(fd); return 67; }
    char *list = malloc(length ? (size_t)length : 1);
    if (!list || names(fd, list, length ? (size_t)length : 1) != length) { free(list); close(fd); return 68; }
    size_t offset = 0, count = 0, total = 12;
    while (offset < (size_t)length) {
        char *end = memchr(list + offset, 0, (size_t)length - offset);
        if (!end || end == list + offset || end - (list + offset) > 255 || ++count > 1024) { free(list); close(fd); return 69; }
        offset = (size_t)(end - list) + 1;
    }
    uint64_t device;
#ifdef __APPLE__
    device = (uint32_t)identity.st_dev;
#else
    device = ((uint64_t)major(identity.st_dev) << 32) | minor(identity.st_dev);
#endif
    printf("FCXOBS1 %016" PRIX64 " %016" PRIX64 "\n", device, (uint64_t)identity.st_ino);
    offset = 0;
    while (offset < (size_t)length) {
        const char *name = list + offset;
        size_t name_bytes = strlen(name);
        ssize_t size = value(fd, name, NULL, 0);
        if (size < 0 || name_bytes + 8 > 1048576 - total || (size_t)size > 1048576 - total - name_bytes - 8) { free(list); close(fd); return 70; }
        unsigned char *bytes = malloc(size ? (size_t)size : 1);
        if (!bytes || value(fd, name, bytes, size ? (size_t)size : 1) != size) { free(bytes); free(list); close(fd); return 71; }
        hex((const unsigned char *)name, name_bytes); putchar(':'); hex(bytes, (size_t)size); putchar('\n');
        total += name_bytes + (size_t)size + 8; offset += name_bytes + 1; free(bytes);
    }
    free(list); return close(fd) == 0 ? 0 : 72;
}
