#define _GNU_SOURCE
#include "bus-peer.h"

#include <assert.h>
#include <dirent.h>
#include <dlfcn.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/socket.h>
#include <sys/prctl.h>
#include <sys/wait.h>
#include <unistd.h>

#define NAME "org.filecat.Permissions.NativeTest"
#define PATH "/org/filecat/Permissions/NativeTest"

enum scenario { direct, wrong_nonce, wrong_process, unexpected_fd, truncated_fds, expired, bus_departure, process_exit };

struct package_case {
    const char *name;
    int load_installed;
    int map_foreign;
    int admit_package;
    int admit_client;
};

static const struct package_case package_cases[] = {
    { "installed", 1, 0, 1, 1 },
    { "portable", 1, 0, 1, 0 },
    { "missing-runtime", 0, 0, 1, 0 },
    { "foreign-runtime", 0, 1, 1, 0 },
    { "additional-runtime", 1, 1, 1, 0 },
    { "untrusted-package", 0, 0, 0, 0 }
};

static int begin(sd_bus_message *message, void *data, sd_bus_error *error)
{
    (void)error;
    struct fc_bus_peer *peer = data;
    int endpoint;
    int r = fc_bus_peer_begin(peer, message, &endpoint);
    if (r >= 0) {
        r = sd_bus_reply_method_return(message, "h", endpoint);
        close(endpoint);
    }
    return r;
}

static const sd_bus_vtable methods[] = {
    SD_BUS_VTABLE_START(0),
    SD_BUS_METHOD("Begin", "", "h", begin, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_VTABLE_END
};

static void wait_readable(int fd)
{
    struct pollfd pending = { .fd = fd, .events = POLLIN };
    assert(poll(&pending, 1, 6000) == 1 && (pending.revents & POLLIN));
}

static void signal_peer(int fd, char message)
{
    assert(write(fd, &message, 1) == 1);
}

static char wait_signal(int fd)
{
    char message;
    wait_readable(fd);
    assert(read(fd, &message, 1) == 1);
    return message;
}

static void respond(int endpoint, enum scenario mode)
{
    unsigned char challenge[32];
    wait_readable(endpoint);
    assert(recv(endpoint, challenge, sizeof(challenge), 0) == sizeof(challenge));
    if (mode == wrong_nonce)
        challenge[0] ^= 1;
    if (mode != unexpected_fd && mode != truncated_fds) {
        assert(send(endpoint, challenge, sizeof(challenge), MSG_NOSIGNAL) == sizeof(challenge));
        return;
    }

    int descriptor = open("/dev/null", O_RDONLY | O_CLOEXEC);
    assert(descriptor >= 0);
    const size_t count = mode == truncated_fds ? 16 : 1;
    union {
        struct cmsghdr align;
        unsigned char bytes[CMSG_SPACE(sizeof(int) * 16)];
    } control;
    struct iovec payload = { .iov_base = challenge, .iov_len = sizeof(challenge) };
    struct msghdr message = {
        .msg_iov = &payload, .msg_iovlen = 1,
        .msg_control = control.bytes, .msg_controllen = CMSG_SPACE(sizeof(int) * count)
    };
    struct cmsghdr *field = CMSG_FIRSTHDR(&message);
    assert(field);
    field->cmsg_level = SOL_SOCKET;
    field->cmsg_type = SCM_RIGHTS;
    field->cmsg_len = CMSG_LEN(sizeof(int) * count);
    for (size_t i = 0; i < count; ++i)
        memcpy(CMSG_DATA(field) + i * sizeof(descriptor), &descriptor, sizeof(descriptor));
    assert(sendmsg(endpoint, &message, MSG_NOSIGNAL) == sizeof(challenge));
    close(descriptor);
}

static void client(enum scenario mode, int controller, int events, const struct package_case *package)
{
    void *runtime = NULL, *foreign = NULL;
    if (package && package->load_installed) {
        runtime = dlopen(FC_INSTALLED_RUNTIME, RTLD_NOW | RTLD_LOCAL);
        if (!runtime)
            fprintf(stderr, "%s\n", dlerror());
        assert(runtime);
    }
    if (package && package->map_foreign) {
        const char *path = getenv("FILECAT_TEST_FOREIGN_RUNTIME");
        assert(path);
        int fd = open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC);
        assert(fd >= 0);
        foreign = mmap(NULL, 4096, PROT_READ | PROT_EXEC, MAP_PRIVATE, fd, 0);
        close(fd);
        assert(foreign != MAP_FAILED);
    }
    sd_bus *bus = NULL;
    sd_bus_message *reply = NULL;
    assert(sd_bus_open_user(&bus) >= 0);
    assert(sd_bus_set_method_call_timeout(bus, 4000000) >= 0);
    assert(sd_bus_call_method(bus, NAME, PATH, NAME, "Begin", NULL, &reply, "") >= 0);
    int endpoint;
    assert(sd_bus_message_read(reply, "h", &endpoint) == 1);
    signal_peer(events, 'r');
    assert(wait_signal(controller) == 's');
    pid_t delegated = -1;
    int hold[2] = { -1, -1 };
    if (mode == wrong_process) {
        assert(pipe2(hold, O_CLOEXEC) == 0);
        delegated = fork();
        assert(delegated >= 0);
        if (delegated == 0) {
            close(hold[1]);
            respond(endpoint, direct);
            assert(wait_signal(hold[0]) == 'q');
            _exit(0);
        }
        close(hold[0]);
    } else if (mode != expired) {
        respond(endpoint, mode);
    }
    char action = wait_signal(controller);
    if (action == 'd') {
        sd_bus_close(bus);
        signal_peer(events, 'd');
        action = wait_signal(controller);
    }
    if (action == 'x') {
        pid_t inheritor = fork();
        assert(inheritor >= 0);
        if (inheritor == 0) {
            signal_peer(events, 'x');
            assert(wait_signal(controller) == 'q');
        }
        _exit(0);
    }
    assert(action == 'q');
    if (delegated > 0) {
        signal_peer(hold[1], 'q');
        close(hold[1]);
        int status;
        assert(waitpid(delegated, &status, 0) == delegated && status == 0);
    }
    sd_bus_message_unref(reply);
    sd_bus_flush_close_unref(bus);
    if (foreign)
        assert(munmap(foreign, 4096) == 0);
    if (runtime)
        assert(dlclose(runtime) == 0);
}

static int open_descriptors(void)
{
    DIR *directory = opendir("/proc/self/fd");
    assert(directory);
    int count = 0;
    while (readdir(directory))
        ++count;
    closedir(directory);
    return count;
}

static void check_executable(const struct fc_bus_peer *proof)
{
    char path[PATH_MAX];
    ssize_t length = readlink("/proc/self/exe", path, sizeof(path));
    assert(length > 0 && (size_t)length < sizeof(path));
    path[length] = '\0';
    int executable = open(path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(executable >= 0);
    int different = open("/proc/self/status", O_PATH | O_CLOEXEC);
    assert(different >= 0);
    assert(fc_bus_peer_check_client(proof, executable, path) == 0);
    assert(fc_bus_peer_check_client(proof, executable, "/different/FileCat") == -EACCES);
    assert(fc_bus_peer_check_client(proof, different, path) == -EACCES);
    close(different);
    close(executable);
}

static void check_package(const struct fc_bus_peer *proof, const struct package_case *expected)
{
    struct fc_installation installation;
    int result = fc_installation_open(&installation);
    if ((result == 0) != expected->admit_package)
        fprintf(stderr, "%s package trust verdict %d\n", expected->name, result);
    assert((result == 0) == expected->admit_package);
    if (result == 0) {
        result = fc_bus_peer_check_installation(proof, &installation);
        if ((result == 0) != expected->admit_client)
            fprintf(stderr, "%s installed peer verdict %d\n", expected->name, result);
        assert((result == 0) == expected->admit_client);
    }
    fc_installation_close(&installation);
}

static void run(enum scenario mode, int verdict, const char *name, const struct package_case *package)
{
    int descriptor_baseline = open_descriptors();
    int controller[2], events[2];
    assert(pipe2(controller, O_CLOEXEC) == 0 && pipe2(events, O_CLOEXEC) == 0);

    sd_bus *bus = NULL;
    sd_bus_slot *slot = NULL;
    struct fc_bus_peer proof = FC_BUS_PEER_INIT;
    assert(sd_bus_open_user(&bus) >= 0);
    assert(sd_bus_set_method_call_timeout(bus, 4000000) >= 0);
    assert(sd_bus_add_object_vtable(bus, &slot, PATH, NAME, methods, &proof) >= 0);
    assert(sd_bus_request_name(bus, NAME, 0) >= 0);
    pid_t child = fork();
    assert(child >= 0);
    if (child == 0) {
        close(controller[1]);
        close(events[0]);
        client(mode, controller[0], events[1], package);
        _exit(0);
    }
    close(controller[0]);
    close(events[1]);
    for (unsigned i = 0; i < 100 && !proof.request; ++i) {
        int r = sd_bus_process(bus, NULL);
        assert(r >= 0);
        if (r == 0)
            assert(sd_bus_wait(bus, 50000) >= 0);
    }
    assert(proof.request && proof.process.pid == child && proof.process.uid == getuid());
    assert(wait_signal(events[0]) == 'r');
    assert(fc_bus_peer_finish(&proof) == -EAGAIN);
    assert(fc_bus_peer_live(&proof) == -EACCES);
    signal_peer(controller[1], 's');

    if (mode == expired) {
        /* Advance the test deadline without delaying the rest of the native cases. */
        proof.process.expires.tv_sec = 0;
    } else {
        wait_readable(proof.process.socket);
    }
    assert(fc_bus_peer_finish(&proof) == verdict);
    if (verdict == 0) {
        assert(proof.process.gid == getgid());
        int flags = fcntl(proof.process.process, F_GETFD);
        assert(flags >= 0 && (flags & FD_CLOEXEC));
        assert(fc_bus_peer_live(&proof) == 0);
        check_executable(&proof);
        if (package)
            check_package(&proof, package);
    } else {
        assert(!proof.request && proof.process.socket == -1 && proof.process.process == -1);
    }

    if (mode == bus_departure) {
        signal_peer(controller[1], 'd');
        assert(wait_signal(events[0]) == 'd');
        for (unsigned i = 0; i < 50 && fc_bus_peer_live(&proof) == 0; ++i)
            usleep(20000);
        assert(fc_bus_peer_live(&proof) < 0);
        assert(fc_peer_live(&proof.process) == 0);
    }
    int status;
    if (mode == process_exit) {
        signal_peer(controller[1], 'x');
        assert(wait_signal(events[0]) == 'x');
        assert(waitpid(child, &status, 0) == child && status == 0);
        sd_bus_creds *credentials = NULL;
        const char *sender = sd_bus_message_get_sender(proof.request);
        assert(sd_bus_get_name_creds(bus, sender, SD_BUS_CREDS_PID | SD_BUS_CREDS_EUID, &credentials) >= 0);
        sd_bus_creds_unref(credentials);
        assert(fc_bus_peer_live(&proof) == -ESRCH);
        signal_peer(controller[1], 'q');
        assert(waitpid(-1, &status, 0) > 0 && status == 0);
    } else {
        signal_peer(controller[1], 'q');
        assert(waitpid(child, &status, 0) == child && status == 0);
    }
    if (verdict == 0)
        assert(fc_peer_live(&proof.process) == -ESRCH);
    fc_bus_peer_close(&proof);
    sd_bus_slot_unref(slot);
    sd_bus_flush_close_unref(bus);
    close(controller[1]);
    close(events[0]);
    assert(open_descriptors() == descriptor_baseline);
    printf("PASS %s\n", name);
}

int main(int argc, char **argv)
{
    assert(getuid() != 0 && getuid() == geteuid());
    assert(prctl(PR_SET_CHILD_SUBREAPER, 1, 0, 0, 0) == 0);
    alarm(30);
    if (argc == 2 && strcmp(argv[1], "package-revision") == 0) {
        struct fc_installation installation;
        assert(fc_installation_open(&installation) == 0);
        assert(puts("READY") >= 0 && fflush(stdout) == 0);
        assert(getchar() == '\n');
        assert(fc_installation_current(&installation) == -ESTALE);
        fc_installation_close(&installation);
        puts("PASS installed configuration replacement invalidated the held package");
        return 0;
    }
    if (argc == 3 && strcmp(argv[1], "package") == 0) {
        for (size_t i = 0; i < sizeof(package_cases) / sizeof(package_cases[0]); ++i) {
            if (strcmp(argv[2], package_cases[i].name) == 0) {
                run(direct, 0, package_cases[i].name, &package_cases[i]);
                return 0;
            }
        }
        return 2;
    }
    assert(argc == 1);
    run(direct, 0, "same D-Bus sender and kernel-authenticated responder", NULL);
    run(wrong_nonce, -EACCES, "incorrect challenge rejected", NULL);
    run(wrong_process, -EACCES, "same UID responding from a different PID rejected", NULL);
    run(unexpected_fd, -EPROTO, "unexpected ancillary descriptor rejected and closed", NULL);
    run(truncated_fds, -EPROTO, "truncated ancillary descriptors rejected and closed", NULL);
    run(expired, -ETIMEDOUT, "expired challenge rejected", NULL);
    run(bus_departure, 0, "D-Bus disappearance rejected while the authenticated process is still alive", NULL);
    run(process_exit, 0, "process exit rejected while inherited descriptors keep its D-Bus name live", NULL);
    return 0;
}
