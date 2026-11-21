#define _GNU_SOURCE
#include "client-identity.h"

#include <errno.h>
#include <fcntl.h>
#include <inttypes.h>
#include <linux/magic.h>
#include <linux/openat2.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/statfs.h>
#include <sys/syscall.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif

static int open_proc_entry(int parent, const char *name, int flags)
{
    const struct open_how how = {
        .flags = (unsigned int)(flags | O_CLOEXEC),
        .resolve = RESOLVE_NO_XDEV | RESOLVE_NO_SYMLINKS | RESOLVE_BENEATH
    };
    long fd = syscall(SYS_openat2, parent, name, &how, sizeof(how));
    return fd >= 0 ? (int)fd : -errno;
}

static int open_process(int proc, pid_t pid)
{
    char name[16];
    int length = snprintf(name, sizeof(name), "%d", pid);
    if (length <= 0 || (size_t)length >= sizeof(name))
        return -EOVERFLOW;
    return open_proc_entry(proc, name, O_PATH | O_DIRECTORY);
}

static int matching_namespace(int own, int client, const char *name)
{
    struct stat left, right;
    if (fstatat(own, name, &left, 0) < 0 || fstatat(client, name, &right, 0) < 0)
        return -errno;
    return left.st_dev == right.st_dev && left.st_ino == right.st_ino ? 0 : -EXDEV;
}

static int matching_namespaces(int own, int client)
{
    const char *names[] = { "ns/user", "ns/mnt", "ns/pid" };
    for (size_t i = 0; i < sizeof(names) / sizeof(names[0]); ++i) {
        int result = matching_namespace(own, client, names[i]);
        if (result < 0)
            return result;
    }
    /* A chroot can change the interpretation of an absolute name without changing mount namespaces. */
    struct statx left, right;
    const unsigned fields = STATX_TYPE | STATX_INO | STATX_MNT_ID_UNIQUE;
    if (statx(own, "root", 0, fields, &left) < 0 || statx(client, "root", 0, fields, &right) < 0)
        return -errno;
    if ((left.stx_mask & fields) != fields || (right.stx_mask & fields) != fields)
        return -EPROTO;
    return S_ISDIR(left.stx_mode) && S_ISDIR(right.stx_mode) && left.stx_dev_major == right.stx_dev_major &&
        left.stx_dev_minor == right.stx_dev_minor && left.stx_ino == right.stx_ino &&
        left.stx_mnt_id == right.stx_mnt_id ? 0 : -EXDEV;
}

static int matching_executable(int process, int executable, const char *path)
{
    char value[PATH_MAX];
    ssize_t length = readlinkat(process, "exe", value, sizeof(value));
    if (length < 0)
        return -errno;
    size_t expected = strlen(path);
    if ((size_t)length >= sizeof(value) || (size_t)length != expected || memcmp(value, path, expected) != 0)
        return -EACCES;

    struct statx trusted, live;
    const unsigned fields = STATX_TYPE | STATX_INO | STATX_MNT_ID;
    if (statx(executable, "", AT_EMPTY_PATH, fields, &trusted) < 0 ||
        statx(process, "exe", 0, fields, &live) < 0)
        return -errno;
    if ((trusted.stx_mask & fields) != fields || (live.stx_mask & fields) != fields ||
        !S_ISREG(trusted.stx_mode) || !S_ISREG(live.stx_mode))
        return -EPROTO;
    return trusted.stx_dev_major == live.stx_dev_major && trusted.stx_dev_minor == live.stx_dev_minor &&
           trusted.stx_ino == live.stx_ino && trusted.stx_mnt_id == live.stx_mnt_id ? 0 : -EACCES;
}

static int match_runtime_mapping(const char *line, const struct statx *runtime, int *executable_map)
{
    uint64_t start, end, offset, inode;
    unsigned major, minor;
    char access[5];
    int path_offset = 0;
    int fields = sscanf(line, "%" SCNx64 "-%" SCNx64 " %4s %" SCNx64 " %x:%x %" SCNu64 " %n",
        &start, &end, access, &offset, &major, &minor, &inode, &path_offset);
    if (fields != 7 || strlen(access) != 4 || path_offset <= 0)
        return -EPROTO;

    const char *path = line + path_offset;
    const char *leaf = strrchr(path, '/');
    if (!leaf || (strcmp(leaf, "/libcoreclr.so") != 0 && strcmp(leaf, "/libcoreclr.so (deleted)") != 0))
        return 0;
    if (strcmp(path, FC_INSTALLED_RUNTIME) != 0 || major != runtime->stx_dev_major ||
        minor != runtime->stx_dev_minor || inode != runtime->stx_ino)
        return -EACCES;
    if (access[2] == 'x')
        *executable_map = 1;
    return 0;
}

static int matching_runtime(int process, const struct statx *runtime)
{
    int descriptor = open_proc_entry(process, "maps", O_RDONLY | O_NONBLOCK);
    if (descriptor < 0)
        return descriptor;
    FILE *maps = fdopen(descriptor, "r");
    if (!maps) {
        int error = errno;
        close(descriptor);
        return -error;
    }

    char line[PATH_MAX + 128];
    size_t total = 0;
    int executable_map = 0, result = 0;
    while (fgets(line, sizeof(line), maps)) {
        size_t length = strlen(line);
        total += length;
        if (length == 0 || line[length - 1] != '\n' || total > 4 * 1024 * 1024) {
            result = -E2BIG;
            break;
        }
        line[length - 1] = '\0';
        result = match_runtime_mapping(line, runtime, &executable_map);
        if (result < 0)
            break;
    }
    if (result == 0 && ferror(maps))
        result = -EIO;
    fclose(maps);
    return result < 0 ? result : executable_map ? 0 : -EACCES;
}

static int read_credentials(int process, const struct fc_peer *peer)
{
    int descriptor = open_proc_entry(process, "status", O_RDONLY | O_NONBLOCK);
    if (descriptor < 0)
        return descriptor;
    char buffer[64 * 1024];
    size_t used = 0;
    ssize_t bytes;
    do {
        bytes = read(descriptor, buffer + used, sizeof(buffer) - used);
        if (bytes > 0)
            used += (size_t)bytes;
    } while (bytes > 0 && used < sizeof(buffer));
    int read_error = bytes < 0 ? -errno : 0;
    close(descriptor);
    if (read_error)
        return read_error;
    if (used == sizeof(buffer))
        return -E2BIG;
    buffer[used] = '\0';

    char *next = NULL;
    unsigned seen = 0;
    for (char *line = strtok_r(buffer, "\n", &next); line; line = strtok_r(NULL, "\n", &next)) {
        unsigned kind;
        if (strncmp(line, "Uid:", 4) == 0)
            kind = 1;
        else if (strncmp(line, "Gid:", 4) == 0)
            kind = 2;
        else
            continue;
        unsigned real, effective, saved, filesystem;
        char extra;
        unsigned expected = kind == 1 ? peer->uid : peer->gid;
        if ((seen & kind) ||
            sscanf(line + 4, "%u %u %u %u %c", &real, &effective, &saved, &filesystem, &extra) != 4 ||
            real != expected || effective != expected || saved != expected || filesystem != expected)
            return -EACCES;
        seen |= kind;
    }
    return seen == 3 ? 0 : -EPROTO;
}

static int check_identity(const struct fc_peer *peer, int executable, const char *path, const struct statx *runtime)
{
    int result = fc_peer_live(peer);
    if (result < 0)
        return result;
    int proc = open("/proc", O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (proc < 0)
        return -errno;
    int own = -1, process = -1;
    struct statfs info;
    if (fstatfs(proc, &info) < 0) {
        result = -errno;
        goto done;
    }
    if (info.f_type != PROC_SUPER_MAGIC) {
        result = -EXDEV;
        goto done;
    }
    own = open_process(proc, getpid());
    if (own < 0) {
        result = own;
        goto done;
    }
    process = open_process(proc, peer->pid);
    if (process < 0) {
        result = process;
        goto done;
    }
    result = matching_namespaces(own, process);
    if (result == 0)
        result = read_credentials(process, peer);
    if (result == 0)
        result = matching_executable(process, executable, path);
    if (result == 0 && runtime)
        result = matching_runtime(process, runtime);
    if (result == 0)
        result = matching_namespaces(own, process);
    if (result == 0)
        result = fc_peer_live(peer);

done:
    if (process >= 0)
        close(process);
    if (own >= 0)
        close(own);
    close(proc);
    return result;
}

int fc_client_identity(const struct fc_peer *peer, int executable, const char *path)
{
    return check_identity(peer, executable, path, NULL);
}

int fc_client_installed_identity(const struct fc_peer *peer, const struct fc_installation *installation)
{
    int result = fc_installation_current(installation);
    if (result == 0)
        result = check_identity(peer, installation->descriptors[FC_INSTALL_EXECUTABLE],
            FC_INSTALLED_EXECUTABLE, &installation->observed[FC_INSTALL_RUNTIME]);
    return result == 0 ? fc_installation_current(installation) : result;
}
