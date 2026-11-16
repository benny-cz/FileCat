// FileCat's macOS archive worker launcher (route decision of 12 November 2026; spike record
// docs/validation/archive-writing/MACOS_SANDBOX_WORKER_SPIKE.md).
//
// It is signed with only the App Sandbox entitlement and carries its own Info.plist (embedded at link time), so the system
// confines this process before main runs. It accepts only the fixed launch facts its parent passes and applies what the
// sandbox leaves open: no new processes, no core files, the CPU allowance, only the parent's four pipes, and no bootstrap
// port, so no Mach service can be looked up by name (the pasteboard, Apple Events, LaunchServices and the others the
// sandbox would allow). The host port stays, as in every process, so the IOKit user clients the App Sandbox allows remain
// reachable. Then it replaces itself with a fixed sibling executable in worker mode. That
// image keeps the sandbox, the limits and the missing bootstrap port across the exec, and checks them all again before it
// reads any input. Every refusal exits 65 with a fixed line on standard error. This program never reads archive data.
#include <fcntl.h>
#include <libproc.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <mach/mach.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <unistd.h>

#define CONTAINER "io.github.benny-cz.filecat.archive-worker"
#define MAX_REQUEST 16
#define MAX_ARGUMENT_BYTES 4096
#define MAX_DESCRIPTORS 1024

extern char **environ;

static int refuse(const char *reason)
{
    // A fixed line for the parent's bounded diagnostics; it never repeats an argument.
    fprintf(stderr, "FileCat archive launcher refused: %s\n", reason);
    return 65;
}

// A decimal number in [minimum, maximum], spelled canonically.
static int decimal(const char *text, long minimum, long maximum, long *value)
{
    size_t length = strlen(text);
    if (length == 0 || length > 10 || (length > 1 && text[0] == '0')) return 0;
    long result = 0;
    for (size_t i = 0; i < length; i++)
    {
        if (text[i] < '0' || text[i] > '9') return 0;
        result = result * 10 + (text[i] - '0');
    }
    if (result < minimum || result > maximum) return 0;
    *value = result;
    return 1;
}

// A task nonce as .NET writes it ("D" format): lower-case hexadecimal digits and four hyphens, not all zero.
static int nonce(const char *text)
{
    if (strlen(text) != 36) return 0;
    int nonzero = 0;
    for (int i = 0; i < 36; i++)
    {
        char c = text[i];
        if (i == 8 || i == 13 || i == 18 || i == 23) { if (c != '-') return 0; continue; }
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return 0;
        if (c != '0') nonzero = 1;
    }
    return nonzero;
}

static int pipe_descriptor(int fd)
{
    struct stat facts;
    return fstat(fd, &facts) == 0 && S_ISFIFO(facts.st_mode);
}

// Owned by root, by this user, or by the account that owns this launcher (an application another user installed in
// /Applications belongs to that user, and whoever runs it already trusts that account), and writable by no one else.
static uid_t launcher_owner = 0;
static int private_object(const struct stat *facts)
{
    return (facts->st_uid == 0 || facts->st_uid == getuid() || facts->st_uid == launcher_owner) &&
        (facts->st_mode & (S_IWGRP | S_IWOTH)) == 0;
}

// The open descriptors of this process, or -1.
static int inventory(struct proc_fdinfo *descriptors)
{
    int bytes = proc_pidinfo(getpid(), PROC_PIDLISTFDS, 0, descriptors, (int)(MAX_DESCRIPTORS * sizeof *descriptors));
    if (bytes <= 0 || bytes >= (int)(MAX_DESCRIPTORS * sizeof *descriptors)) return -1;
    return bytes / (int)sizeof *descriptors;
}

int main(int argc, char **argv)
{
    // FileCatArchiveWorker --archive-mac-launch IMAGE NONCE PARENT CPU REQUEST... --archive-cancellation-handle FD
    if (argc < 9 || argc > 8 + MAX_REQUEST) return refuse("invalid launch facts");
    for (int i = 1; i < argc; i++)
        if (strlen(argv[i]) > MAX_ARGUMENT_BYTES) return refuse("invalid launch facts");
    long parent, cpu, cancellation;
    const char *image = argv[2];
    if (strcmp(argv[1], "--archive-mac-launch") != 0 || (strcmp(image, "FileCat") != 0 && strcmp(image, "FileCat.WorkerProbe") != 0) ||
        !nonce(argv[3]) || !decimal(argv[4], 2, INT_MAX, &parent) || !decimal(argv[5], 1, 7200, &cpu) ||
        strcmp(argv[argc - 2], "--archive-cancellation-handle") != 0 || !decimal(argv[argc - 1], 3, MAX_DESCRIPTORS - 1, &cancellation))
        return refuse("invalid launch facts");
    for (char **entry = environ; *entry != NULL; entry++)
        if (strncmp(*entry, "DYLD_", 5) == 0) return refuse("loader environment");
    // The system sets this while it applies the sandbox: without it the entitlement is missing and nothing may run.
    const char *container = getenv("APP_SANDBOX_CONTAINER_ID");
    if (container == NULL || strcmp(container, CONTAINER) != 0) return refuse("not inside the App Sandbox");
    if ((long)getppid() != parent) return refuse("unexpected parent");
    int fd = (int)cancellation, flags = fcntl(fd, F_GETFL);
    if (!pipe_descriptor(0) || !pipe_descriptor(1) || !pipe_descriptor(2)) return refuse("standard streams are not the parent's pipes");
    if (!pipe_descriptor(fd) || flags < 0 || (flags & O_ACCMODE) != O_RDONLY) return refuse("invalid cancellation pipe");

    // Keep only the parent's four pipes: every other inherited descriptor is closed before the worker image starts.
    static struct proc_fdinfo descriptors[MAX_DESCRIPTORS];
    int count = inventory(descriptors);
    if (count < 0) return refuse("descriptor inventory unavailable");
    for (int i = 0; i < count; i++)
    {
        int open = descriptors[i].proc_fd;
        if (open > 2 && open != fd && close(open) != 0) return refuse("an inherited descriptor could not be closed");
    }
    count = inventory(descriptors);
    if (count != 4) return refuse("unexpected descriptors remain");
    for (int i = 0; i < count; i++)
    {
        int open = descriptors[i].proc_fd;
        if ((open < 0 || open > 2) && open != fd) return refuse("unexpected descriptors remain");
        if (fcntl(open, F_SETFD, 0) != 0) return refuse("a parent pipe could not be kept");
    }

    // What the sandbox does not limit: new processes (it allows them), core files, writes in the worker's own container
    // (the one place it may write; a file-size limit of zero leaves only empty files there, and an attempt to write data
    // ends the worker, as on Linux, where the worker writes nowhere) and processor time.
    struct rlimit none = { 0, 0 }, allowance = { (rlim_t)cpu, (rlim_t)cpu };
    if (setrlimit(RLIMIT_NPROC, &none) != 0 || setrlimit(RLIMIT_CORE, &none) != 0 || setrlimit(RLIMIT_FSIZE, &none) != 0 ||
        setrlimit(RLIMIT_CPU, &allowance) != 0)
        return refuse("limits could not be applied");

    // The worker image: a fixed name beside this launcher, in a folder no one else can change.
    char self[PATH_MAX], folder[PATH_MAX], target[PATH_MAX];
    uint32_t size = sizeof self;
    if (_NSGetExecutablePath(self, &size) != 0 || realpath(self, folder) == NULL) return refuse("launcher path unavailable");
    char *slash = strrchr(folder, '/');
    if (slash == NULL || slash == folder) return refuse("launcher path unavailable");
    struct stat facts;
    if (stat(folder, &facts) != 0 || !S_ISREG(facts.st_mode) || (facts.st_mode & (S_IWGRP | S_IWOTH)) != 0)
        return refuse("the launcher is not a private part of this application");
    launcher_owner = facts.st_uid;
    *slash = '\0';
    if (snprintf(target, sizeof target, "%s/%s", folder, image) >= (int)sizeof target) return refuse("worker path too long");
    if (lstat(folder, &facts) != 0 || !S_ISDIR(facts.st_mode) || !private_object(&facts) ||
        lstat(target, &facts) != 0 || !S_ISREG(facts.st_mode) || !private_object(&facts))
        return refuse("the worker image is not a private part of this application");

    // No Mach service lookup from here on: the pasteboard, Apple Events, LaunchServices and the others the sandbox allows.
    if (task_set_special_port(mach_task_self(), TASK_BOOTSTRAP_PORT, MACH_PORT_NULL) != KERN_SUCCESS)
        return refuse("the bootstrap port could not be released");

    char *worker[2 + MAX_REQUEST + 6];
    int used = 0;
    worker[used++] = target;
    worker[used++] = "--archive-mac-worker";
    for (int i = 3; i < argc; i++) worker[used++] = argv[i]; // NONCE PARENT CPU REQUEST... --archive-cancellation-handle FD
    worker[used] = NULL;
    execve(target, worker, environ);
    return refuse("the worker image could not start");
}
