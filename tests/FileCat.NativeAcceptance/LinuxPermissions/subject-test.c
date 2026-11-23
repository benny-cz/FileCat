#define _GNU_SOURCE
#include "client-identity.h"
#include "subject.h"

#include <assert.h>
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <linux/mount.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/wait.h>
#include <sys/xattr.h>
#include <systemd/sd-bus.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif

static const char bus_name[] = "org.filecat.Permissions.SubjectTest";
static const char bus_path[] = "/org/filecat/Permissions/SubjectTest";

static struct fc_subject_identity capture(int fd)
{
    struct statx facts;
    unsigned mask = STATX_TYPE | STATX_INO | STATX_MNT_ID_UNIQUE;
    assert(statx(fd, "", AT_EMPTY_PATH, mask, &facts) == 0 && (facts.stx_mask & mask) == mask);
    return (struct fc_subject_identity) {
        facts.stx_mnt_id, facts.stx_ino, facts.stx_dev_major, facts.stx_dev_minor, facts.stx_mode & S_IFMT
    };
}

static void make_path(char *path, const char *root, const char *name)
{
    int length = snprintf(path, FC_SUBJECT_PATH_BYTES, "%s/%s", root, name);
    assert(length > 0 && length < FC_SUBJECT_PATH_BYTES);
}

static int path_descriptor(const char *path)
{
    int result = open(path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(result >= 0);
    return result;
}

static void create_file(const char *path)
{
    int fd = open(path, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC, 0600);
    assert(fd >= 0 && write(fd, "data", 4) == 4);
    close(fd);
}

static size_t descriptor_count(void)
{
    DIR *directory = opendir("/proc/self/fd");
    assert(directory);
    size_t count = 0;
    while (readdir(directory))
        ++count;
    closedir(directory);
    return count;
}

struct received_subject {
    int received;
    int result;
    struct fc_subject hold;
};

static int method_bind(sd_bus_message *request, void *data, sd_bus_error *error)
{
    (void)error;
    struct received_subject *received = data;
    int fd;
    const char *name;
    struct fc_subject_identity expected;
    assert(sd_bus_message_read(request, "hsuuttq", &fd, &name, &expected.device_major, &expected.device_minor,
        &expected.mount, &expected.inode, &expected.kind) > 0);
    assert(sd_bus_message_at_end(request, 1) > 0);
    struct fc_subject_state state;
    received->result = fc_subject_bind(&received->hold, fd, name, &expected, &state);
    received->received = 1;
    return sd_bus_reply_method_return(request, "i", received->result);
}

static const sd_bus_vtable methods[] = {
    SD_BUS_VTABLE_START(0),
    SD_BUS_METHOD("Bind", "hsuuttq", "i", method_bind, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_VTABLE_END
};

static void test_transfer(const char *path, int mode_pin)
{
    size_t descriptors = descriptor_count();
    struct received_subject test = { .hold = FC_SUBJECT_INIT };
    sd_bus *server = NULL;
    sd_bus_slot *slot = NULL;
    assert(sd_bus_open_user(&server) >= 0);
    assert(sd_bus_add_object_vtable(server, &slot, bus_path, bus_name, methods, &test) >= 0);
    assert(sd_bus_request_name(server, bus_name, 0) >= 0);
    pid_t requester = fork();
    assert(requester >= 0);
    if (requester == 0) {
        sd_bus *client = NULL;
        sd_bus_message *reply = NULL;
        struct fc_subject_identity expected = capture(mode_pin);
        assert(sd_bus_open_user(&client) >= 0);
        assert(sd_bus_set_method_call_timeout(client, 5000000) >= 0);
        assert(sd_bus_call_method(client, bus_name, bus_path, bus_name, "Bind", NULL, &reply, "hsuuttq",
            mode_pin, path, expected.device_major, expected.device_minor, expected.mount, expected.inode, expected.kind) >= 0);
        int result;
        assert(sd_bus_message_read(reply, "i", &result) > 0 && result == 0);
        close(mode_pin);
        sd_bus_message_unref(reply);
        sd_bus_flush_close_unref(client);
        _exit(0);
    }
    for (unsigned i = 0; i < 100 && !test.received; ++i) {
        int r = sd_bus_process(server, NULL);
        assert(r >= 0);
        if (r == 0)
            assert(sd_bus_wait(server, 50000) >= 0);
    }
    assert(test.received && test.result == 0);
    int status;
    assert(waitpid(requester, &status, 0) == requester && status == 0);
    sd_bus_slot_unref(slot);
    sd_bus_flush_close_unref(server);
    struct fc_subject_state state;
    assert(fc_subject_observe(&test.hold, &state) == 0 && (state.facts.stx_mode & 0777) == 0);
    fc_subject_close(&test.hold);
    assert(descriptor_count() == descriptors);
    puts("PASS D-Bus transferred mode-000 pin remains bound after the sender and message close");
}

static void test_subject(const char *root)
{
    size_t descriptors = descriptor_count();
    char file[FC_SUBJECT_PATH_BYTES], other[FC_SUBJECT_PATH_BYTES], alias[FC_SUBJECT_PATH_BYTES];
    make_path(file, root, "target");
    make_path(other, root, "other");
    make_path(alias, root, "alias");
    create_file(file);
    create_file(other);
    int pin = path_descriptor(file);
    int data = open(file, O_RDONLY | O_CLOEXEC);
    assert(data >= 0);
    assert(chmod(file, 0) == 0);
    struct fc_subject_identity expected = capture(pin);
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_subject_state state;
    assert(fc_subject_bind(&subject, data, file, &expected, &state) == -EBADF);
    close(data);
    assert(fc_subject_bind(&subject, pin, other, &expected, &state) == -ESTALE && subject.pin == -1);
    struct fc_subject_identity wrong_mount = expected;
    ++wrong_mount.mount;
    assert(fc_subject_bind(&subject, pin, file, &wrong_mount, &state) == -ESTALE && subject.pin == -1);
    char ambiguous[FC_SUBJECT_PATH_BYTES];
    make_path(ambiguous, root, "./target");
    assert(fc_subject_bind(&subject, pin, ambiguous, &expected, &state) == -EINVAL);
    make_path(ambiguous, root, "target/");
    assert(fc_subject_bind(&subject, pin, ambiguous, &expected, &state) == -EINVAL);
    puts("PASS data descriptors, substituted objects, incorrect mount evidence and ambiguous paths rejected");

    test_transfer(file, pin);
    assert(fc_subject_bind(&subject, pin, file, &expected, &state) == 0);
    struct fc_subject_state second;
    assert(fc_subject_bind(&subject, pin, other, &expected, &second) == -EALREADY);
    close(pin);
    int flags = fcntl(subject.pin, F_GETFD);
    assert(flags >= 0 && (flags & FD_CLOEXEC));
    assert(fc_subject_check_write(&subject, &state, getuid(), 0) == 0);
    assert(chmod(file, 0600) == 0 && fc_subject_check_write(&subject, &state, getuid(), 0) == -ESTALE);
    assert(fc_subject_observe(&subject, &state) == 0);
    assert(setxattr(file, "user.filecat-test", "x", 1, 0) == 0);
    assert(fc_subject_check_write(&subject, &state, getuid(), 0) == -ESTALE);
    puts("PASS changes to mode and xattrs invalidate the previous subject observation");
    assert(link(file, alias) == 0);
    assert(fc_subject_observe(&subject, &state) == 0 && state.facts.stx_nlink == 2);
    assert(fc_subject_check_write(&subject, &state, getuid() + 1, 0) == -EMLINK);
    assert(fc_subject_check_write(&subject, &state, getuid() + 1, 1) == 0);
    assert(fc_subject_check_write(&subject, &state, getuid(), 0) == 0);
    fc_subject_close(&subject);
    puts("PASS alias guard distinguishes non-owner widening, restrictive changes and ownership");

    assert(unlink(alias) == 0 && symlink(file, alias) == 0);
    int symlink_pin = path_descriptor(alias);
    struct fc_subject_identity linked = capture(symlink_pin);
    assert(fc_subject_bind(&subject, symlink_pin, alias, &linked, &state) == -EOPNOTSUPP);
    close(symlink_pin);
    assert(unlink(alias) == 0 && mkfifo(alias, 0600) == 0);
    int fifo = path_descriptor(alias);
    struct fc_subject_identity pipe_info = capture(fifo);
    assert(fc_subject_bind(&subject, fifo, alias, &pipe_info, &state) == -EOPNOTSUPP);
    close(fifo);
    puts("PASS symlink and FIFO are refused from metadata pins");

    char parent[FC_SUBJECT_PATH_BYTES], renamed[FC_SUBJECT_PATH_BYTES], original[FC_SUBJECT_PATH_BYTES], new_name[FC_SUBJECT_PATH_BYTES];
    make_path(parent, root, "parent");
    make_path(renamed, root, "captured-parent");
    assert(mkdir(parent, 0700) == 0);
    make_path(original, parent, "child");
    create_file(original);
    int child = path_descriptor(original);
    struct fc_subject_identity child_id = capture(child);
    assert(fc_subject_bind(&subject, child, original, &child_id, &state) == 0);
    assert(rename(parent, renamed) == 0 && mkdir(parent, 0700) == 0);
    make_path(new_name, renamed, "child");
    assert(link(new_name, original) == 0);
    assert(fc_subject_observe(&subject, &state) == -ESTALE);
    close(child);
    fc_subject_close(&subject);
    assert(descriptor_count() == descriptors);
    puts("PASS replacement ancestry refused even when the exact leaf inode is still present");
}

static void test_mounts(char **paths)
{
    struct fc_subject_state state;
    struct fc_subject subject = FC_SUBJECT_INIT;
    int pin = path_descriptor(paths[0]);
    int mounted = path_descriptor(paths[1]);
    struct fc_subject_identity base = capture(pin), other = capture(mounted);
    assert(base.inode == other.inode && base.device_major == other.device_major &&
        base.device_minor == other.device_minor && base.mount != other.mount);
    int result = fc_subject_bind(&subject, pin, paths[1], &other, &state);
    assert(result == -EXDEV && subject.pin == -1);
    assert(fc_subject_bind(&subject, mounted, paths[1], &other, &state) == 0);
    fc_subject_close(&subject);
    close(mounted);
    close(pin);
    puts("PASS mounted ext4 accepted while another instance of the same inode is refused");

    int read_only = path_descriptor(paths[2]);
    struct fc_subject_identity read_identity = capture(read_only);
    assert(fc_subject_bind(&subject, read_only, paths[2], &read_identity, &state) == 0);
    assert((state.mount_attributes & MOUNT_ATTR_RDONLY) && fc_subject_check_write(&subject, &state, 1000, 0) == -EROFS);
    fc_subject_close(&subject);
    close(read_only);
    puts("PASS read-only ext4 mount can be observed and refuses writes");

    int idmapped = path_descriptor(paths[3]);
    struct fc_subject_identity mapped_identity = capture(idmapped);
    assert(fc_subject_bind(&subject, idmapped, paths[3], &mapped_identity, &state) == -EOPNOTSUPP);
    close(idmapped);
    puts("PASS kernel-reported idmapped ext4 mount refused");

    int unsupported = path_descriptor(paths[4]);
    struct fc_subject_identity unsupported_identity = capture(unsupported);
    assert(fc_subject_bind(&subject, unsupported, paths[4], &unsupported_identity, &state) == -EXDEV);
    close(unsupported);
    puts("PASS tmpfs mount refused by the filesystem check");
}

static void test_different_root(const char *root)
{
    int channel[2];
    assert(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, channel) == 0);
    pid_t process = fork();
    assert(process >= 0);
    if (process == 0) {
        close(channel[0]);
        assert(chroot(root) == 0 && chdir("/") == 0);
        assert(setgroups(0, NULL) == 0 && setgid(1000) == 0 && setuid(1000) == 0);
        assert(write(channel[1], "r", 1) == 1);
        char finish;
        assert(read(channel[1], &finish, 1) == 1 && finish == 'q');
        _exit(0);
    }
    close(channel[1]);
    char ready;
    assert(read(channel[0], &ready, 1) == 1 && ready == 'r');
    struct fc_peer peer = FC_PEER_INIT;
    peer.process = (int)syscall(SYS_pidfd_open, process, 0);
    assert(peer.process >= 0);
    peer.socket = channel[0];
    peer.pid = process;
    peer.uid = 1000;
    peer.gid = 1000;
    peer.verified = 1;
    char path[FC_SUBJECT_PATH_BYTES];
    ssize_t length = readlink("/proc/self/exe", path, sizeof(path));
    assert(length > 0 && (size_t)length < sizeof(path));
    path[length] = '\0';
    int executable = path_descriptor(path);
    int result = fc_client_identity(&peer, executable, path);
    close(executable);
    assert(write(channel[0], "q", 1) == 1);
    int status;
    assert(waitpid(process, &status, 0) == process && status == 0);
    fc_peer_close(&peer);
    assert(result == -EXDEV);
    puts("PASS peer with a different filesystem root refused within the same mount namespace");
}

int main(int argc, char **argv)
{
    alarm(20);
    if (argc == 2)
        test_subject(argv[1]);
    else if (argc == 7 && strcmp(argv[1], "mounts") == 0)
        test_mounts(argv + 2);
    else if (argc == 3 && strcmp(argv[1], "root") == 0)
        test_different_root(argv[2]);
    else
        return 2;
    return 0;
}
