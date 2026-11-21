#define _GNU_SOURCE
#include "subject.h"

#include <errno.h>
#include <fcntl.h>
#include <linux/magic.h>
#include <linux/mount.h>
#include <linux/openat2.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/statfs.h>
#include <sys/statvfs.h>
#include <sys/syscall.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif
#ifndef SYS_statmount
#define SYS_statmount 457
#endif

/* Linux x64 statmount v0 ABI. Later versions fill reserved output fields. */
struct mount_request {
    uint32_t size, reserved;
    uint64_t mount, mask;
};

struct mount_status {
    uint32_t size, reserved0;
    uint64_t mask;
    uint32_t device_major, device_minor;
    uint64_t magic;
    uint32_t superblock_flags, fs_type;
    uint64_t mount, parent;
    uint32_t old_mount, old_parent;
    uint64_t attributes, propagation, peer_group, master, propagate_from;
    uint32_t mount_root, mount_point;
    unsigned char reserved[400];
    char strings[FC_SUBJECT_PATH_BYTES + 16];
};

_Static_assert(sizeof(struct mount_request) == 24, "statmount request ABI");
_Static_assert(offsetof(struct mount_status, strings) == 512, "statmount string buffer ABI");

enum { mount_superblock = 1, mount_basic = 2, mount_point = 16, mount_filesystem = 32 };
static const unsigned stat_fields =
    STATX_TYPE | STATX_MODE | STATX_NLINK | STATX_UID | STATX_GID | STATX_INO | STATX_SIZE |
    STATX_CTIME | STATX_MTIME | STATX_MNT_ID_UNIQUE;

static int open_component(int parent, const char *name, int flags, uint64_t resolve)
{
    const struct open_how how = {
        .flags = (unsigned int)(flags | O_CLOEXEC | O_NOFOLLOW),
        .resolve = RESOLVE_BENEATH | RESOLVE_NO_SYMLINKS | resolve
    };
    long descriptor = syscall(SYS_openat2, parent, name, &how, sizeof(how));
    return descriptor < 0 ? -errno : (int)descriptor;
}

int fc_linux_own_proc_thread(void)
{
    int proc = open("/proc", O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (proc < 0)
        return -errno;
    struct statfs filesystem;
    int result = -EXDEV;
    if (fstatfs(proc, &filesystem) == 0 && filesystem.f_type == PROC_SUPER_MAGIC) {
        char thread[48], actual[48];
        int length = snprintf(thread, sizeof(thread), "%d/task/%ld", getpid(), syscall(SYS_gettid));
        if (length > 0 && (size_t)length < sizeof(thread) &&
            readlinkat(proc, "thread-self", actual, sizeof(actual)) == length &&
            memcmp(thread, actual, (size_t)length) == 0)
            result = open_component(proc, thread, O_PATH | O_DIRECTORY, RESOLVE_NO_XDEV);
    }
    close(proc);
    return result;
}

int fc_linux_proc_entry(int proc, const char *name, int flags)
{
    return open_component(proc, name, flags, RESOLVE_NO_XDEV);
}

/* O_PATH fdinfo reports the kernel's mount ID without invoking the held filesystem's getattr. */
static int descriptor_mount(int info, int descriptor, unsigned *mount)
{
    char name[16], line[128];
    int length = snprintf(name, sizeof(name), "%d", descriptor);
    if (length <= 0 || (size_t)length >= sizeof(name))
        return -EINVAL;
    int file = fc_linux_proc_entry(info, name, O_RDONLY | O_NONBLOCK);
    if (file < 0)
        return file;
    ssize_t size = read(file, line, sizeof(line) - 1);
    int result = size < 0 ? -errno : -EPROTO;
    close(file);
    if (size > 0) {
        line[size] = '\0';
        const char *field = strstr(line, "\nmnt_id:");
        char end;
        if (field && sscanf(field, "\nmnt_id: %u%c", mount, &end) == 2 && *mount != 0 && end == '\n')
            result = 0;
    }
    return result;
}

static int mount_is_ext4(int proc, unsigned wanted)
{
    int fd = fc_linux_proc_entry(proc, "mountinfo", O_RDONLY | O_NONBLOCK);
    if (fd < 0)
        return fd;
    FILE *file = fdopen(fd, "r");
    if (!file) {
        int error = errno;
        close(fd);
        return -error;
    }
    char line[4 * FC_SUBJECT_PATH_BYTES];
    size_t total = 0;
    int result = -EXDEV;
    while (fgets(line, sizeof(line), file)) {
        size_t length = strlen(line);
        total += length;
        if (length == 0 || line[length - 1] != '\n' || total > 4 * 1024 * 1024) {
            result = -E2BIG;
            break;
        }
        unsigned id;
        char separator;
        if (sscanf(line, "%u%c", &id, &separator) != 2 || separator != ' ') {
            result = -EPROTO;
            break;
        }
        if (id == wanted) {
            const char *type = strstr(line, " - ");
            result = type && strncmp(type + 3, "ext4 ", 5) == 0 ? 0 : -EXDEV;
            break;
        }
    }
    if (ferror(file))
        result = -EIO;
    fclose(file);
    return result;
}

static int read_facts_at(int descriptor, const char *name, struct statx *facts)
{
    int flags = AT_NO_AUTOMOUNT | (name[0] ? 0 : AT_EMPTY_PATH);
    if (statx(descriptor, name, flags, stat_fields, facts) < 0)
        return -errno;
    if ((facts->stx_mask & stat_fields) != stat_fields || facts->stx_mnt_id == 0 ||
        facts->stx_nlink == 0 || facts->stx_uid == UINT32_MAX || facts->stx_gid == UINT32_MAX)
        return -ESTALE;
    if ((facts->stx_attributes & STATX_ATTR_AUTOMOUNT) ||
        (!S_ISREG(facts->stx_mode) && !S_ISDIR(facts->stx_mode)))
        return -EOPNOTSUPP;
    return 0;
}

static int read_facts(int descriptor, struct statx *facts)
{
    return read_facts_at(descriptor, "", facts);
}

static const char *mount_string(const struct mount_status *status, uint32_t offset)
{
    size_t used = status->size - offsetof(struct mount_status, strings);
    if (offset >= used || !memchr(status->strings + offset, '\0', used - offset))
        return NULL;
    return status->strings + offset;
}

static int attest_mount(int descriptor, unsigned old_id, const struct statx *facts,
    const char *path, size_t prefix, uint64_t *attributes)
{
    struct mount_request request = {
        .size = sizeof(request), .mount = facts->stx_mnt_id,
        .mask = mount_superblock | mount_basic | mount_point | mount_filesystem
    };
    struct mount_status status;
    if (syscall(SYS_statmount, &request, &status, sizeof(status), 0) < 0)
        return -errno;
    if (status.size < offsetof(struct mount_status, strings) || status.size > sizeof(status) ||
        (status.mask & request.mask) != request.mask || status.mount != facts->stx_mnt_id ||
        status.old_mount != old_id || status.device_major != facts->stx_dev_major ||
        status.device_minor != facts->stx_dev_minor || status.magic != EXT4_SUPER_MAGIC)
        return -EXDEV;
    const char *type = mount_string(&status, status.fs_type);
    const char *point = mount_string(&status, status.mount_point);
    if (!type || strcmp(type, "ext4") != 0 || !point || strlen(point) != prefix || memcmp(point, path, prefix) != 0)
        return -EXDEV;
    if (status.attributes & MOUNT_ATTR_IDMAP)
        return -EOPNOTSUPP;
    struct statfs filesystem;
    if (fstatfs(descriptor, &filesystem) < 0)
        return -errno;
    if (filesystem.f_type != EXT4_SUPER_MAGIC)
        return -EXDEV;
    *attributes = status.attributes;
    if ((status.superblock_flags & MS_RDONLY) || (filesystem.f_flags & ST_RDONLY))
        *attributes |= MOUNT_ATTR_RDONLY;
    return 0;
}

static struct fc_subject_identity identity(const struct statx *facts)
{
    return (struct fc_subject_identity) {
        .mount = facts->stx_mnt_id, .inode = facts->stx_ino, .device_major = facts->stx_dev_major,
        .device_minor = facts->stx_dev_minor, .kind = facts->stx_mode & S_IFMT
    };
}

static int same_identity(struct fc_subject_identity a, struct fc_subject_identity b)
{
    return a.mount == b.mount && a.inode == b.inode && a.device_major == b.device_major &&
        a.device_minor == b.device_minor && a.kind == b.kind;
}

static int same_facts(const struct statx *a, const struct statx *b)
{
    return same_identity(identity(a), identity(b)) &&
        a->stx_uid == b->stx_uid && a->stx_gid == b->stx_gid && a->stx_mode == b->stx_mode &&
        a->stx_nlink == b->stx_nlink && a->stx_size == b->stx_size &&
        a->stx_ctime.tv_sec == b->stx_ctime.tv_sec && a->stx_ctime.tv_nsec == b->stx_ctime.tv_nsec &&
        a->stx_mtime.tv_sec == b->stx_mtime.tv_sec && a->stx_mtime.tv_nsec == b->stx_mtime.tv_nsec &&
        a->stx_attributes == b->stx_attributes && a->stx_attributes_mask == b->stx_attributes_mask;
}

int fc_subject_state_matches(const struct fc_subject_state *left, const struct fc_subject_state *right)
{
    return left->mount_attributes == right->mount_attributes && same_facts(&left->facts, &right->facts);
}

int fc_subject_verify_proc_alias(int directory, const char *name, const struct fc_subject_state *expected)
{
    struct statx current;
    int result = read_facts_at(directory, name, &current);
    if (result < 0)
        return result;
    return same_facts(&expected->facts, &current) ? 0 : -ESTALE;
}

static int valid_path(const char *path)
{
    if (path[0] != '/' || strnlen(path, FC_SUBJECT_PATH_BYTES) == FC_SUBJECT_PATH_BYTES)
        return 0;
    if (!path[1])
        return 1;
    size_t depth = 0;
    for (const char *component = path + 1; ; ) {
        size_t length = strcspn(component, "/");
        if (++depth > FC_SUBJECT_PATH_DEPTH || length == 0 ||
            (length == 1 && component[0] == '.') ||
            (length == 2 && component[0] == '.' && component[1] == '.'))
            return 0;
        if (!component[length])
            return 1;
        component += length + 1;
    }
}

static int walk(const struct fc_subject *subject, struct fc_subject *capture,
    int proc, int info, struct fc_subject_state *state, unsigned *leaf_mount, int *local_pin)
{
    int descriptor = open("/", O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (descriptor < 0)
        return -errno;
    const char *path = subject->path;
    size_t prefix = 1, depth = 0;
    unsigned previous_mount = 0, now_mount;
    struct fc_subject_identity mount_root = { 0 };
    int result;
    for (;;) {
        result = descriptor_mount(info, descriptor, &now_mount);
        if (result < 0)
            break;
        if (now_mount != previous_mount) {
            result = mount_is_ext4(proc, now_mount);
            if (result < 0)
                break;
        }
        result = read_facts(descriptor, &state->facts);
        if (result < 0)
            break;
        struct fc_subject_identity current = identity(&state->facts);
        if (now_mount != previous_mount) {
            result = attest_mount(descriptor, now_mount, &state->facts, path, prefix, &state->mount_attributes);
            if (result < 0)
                break;
            mount_root = current;
            previous_mount = now_mount;
        } else if (current.mount != mount_root.mount || current.device_major != mount_root.device_major ||
                   current.device_minor != mount_root.device_minor) {
            result = -EXDEV;
            break;
        }
        if (capture) {
            capture->components[depth] = current;
            capture->depth = depth;
        } else if (depth > subject->depth || !same_identity(current, subject->components[depth])) {
            result = -ESTALE;
            break;
        }
        if (!path[prefix]) {
            *leaf_mount = now_mount;
            *local_pin = descriptor;
            return 0;
        }
        if (!S_ISDIR(current.kind)) {
            result = -ENOTDIR;
            break;
        }
        const char *component = path + (depth == 0 ? prefix : prefix + 1);
        size_t name_size = strcspn(component, "/");
        char name[FC_SUBJECT_PATH_BYTES];
        memcpy(name, component, name_size);
        name[name_size] = '\0';
        /* Keep each component terminal so O_PATH cannot trigger an automount. */
        int next = open_component(descriptor, name, O_PATH, 0);
        if (next < 0) {
            result = next;
            break;
        }
        close(descriptor);
        descriptor = next;
        prefix = (size_t)(component - path) + name_size;
        ++depth;
    }
    close(descriptor);
    return result;
}

static int inspect(const struct fc_subject *subject, struct fc_subject *capture,
    struct fc_subject_state *state)
{
    int proc = fc_linux_own_proc_thread();
    if (proc < 0)
        return proc;
    int info = fc_linux_proc_entry(proc, "fdinfo", O_PATH | O_DIRECTORY);
    if (info < 0) {
        close(proc);
        return info;
    }
    unsigned local, held;
    int local_pin = -1;
    int result = walk(subject, capture, proc, info, state, &local, &local_pin);
    if (result == 0)
        result = descriptor_mount(info, subject->pin, &held);
    if (result == 0 && local != held)
        result = -EXDEV;
    if (result == 0) {
        /* Both mounts are pinned, so their legacy mount IDs cannot be recycled during this comparison. */
        struct statx retained;
        result = read_facts(subject->pin, &retained);
        if (result == 0 && !same_facts(&state->facts, &retained))
            result = -ESTALE;
    }
    if (local_pin >= 0)
        close(local_pin);
    close(info);
    close(proc);
    return result;
}

int fc_subject_bind(struct fc_subject *subject, int received, const char *path,
    const struct fc_subject_identity *reviewed, struct fc_subject_state *state)
{
    if (subject->pin >= 0)
        return -EALREADY;
    if (!valid_path(path))
        return -EINVAL;
    int flags = fcntl(received, F_GETFL);
    if (flags < 0)
        return -errno;
    if ((flags & O_PATH) == 0)
        return -EBADF;
    subject->pin = fcntl(received, F_DUPFD_CLOEXEC, 3);
    if (subject->pin < 0)
        return -errno;
    strcpy(subject->path, path);
    int result = inspect(subject, subject, state);
    if (result == 0 && !same_identity(identity(&state->facts), *reviewed))
        result = -ESTALE;
    if (result == 0) {
        struct fc_subject_state checked;
        result = fc_subject_observe(subject, &checked);
        if (result == 0 && !fc_subject_state_matches(state, &checked))
            result = -ESTALE;
    }
    if (result < 0)
        fc_subject_close(subject);
    return result;
}

int fc_subject_observe(const struct fc_subject *subject, struct fc_subject_state *state)
{
    return subject->pin >= 0 ? inspect(subject, NULL, state) : -EBADF;
}

int fc_subject_check_write(const struct fc_subject *subject, const struct fc_subject_state *reviewed,
    uid_t requester, int strictly_restrictive)
{
    struct fc_subject_state current;
    int result = fc_subject_observe(subject, &current);
    if (result < 0)
        return result;
    if (current.mount_attributes & MOUNT_ATTR_RDONLY)
        return -EROFS;
    if (!fc_subject_state_matches(&current, reviewed))
        return -ESTALE;
    if (S_ISREG(current.facts.stx_mode) && current.facts.stx_nlink > 1 &&
        current.facts.stx_uid != requester && !strictly_restrictive)
        return -EMLINK;
    return 0;
}

void fc_subject_close(struct fc_subject *subject)
{
    if (subject->pin >= 0)
        close(subject->pin);
    subject->pin = -1;
}
