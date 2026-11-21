#define _GNU_SOURCE
#include "installation.h"

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static const char *const package_files[] = {
    [FC_INSTALL_EXECUTABLE] = FC_INSTALLED_EXECUTABLE,
    [FC_INSTALL_RUNTIME] = FC_INSTALLED_RUNTIME,
    [FC_INSTALL_CONFIG] = "/opt/filecat/FileCat.runtimeconfig.json",
    [FC_INSTALL_MECHANISM] = "/opt/filecat/FileCat.Permissions.Linux",
    [FC_INSTALL_BUS_POLICY] = "/usr/share/dbus-1/system.d/org.filecat.Permissions.conf",
    [FC_INSTALL_BUS_SERVICE] = "/usr/share/dbus-1/system-services/org.filecat.Permissions.service",
    [FC_INSTALL_POLKIT_ACTIONS] = "/usr/share/polkit-1/actions/org.filecat.permissions.policy",
    [FC_INSTALL_SYSTEMD_UNIT] = "/usr/lib/systemd/system/filecat-permissions.service"
};

static int protected_directory(int descriptor)
{
    struct stat found;
    if (fstat(descriptor, &found) < 0)
        return -errno;
    return S_ISDIR(found.st_mode) && found.st_uid == 0 && (found.st_mode & 022) == 0 ? 0 : -EACCES;
}

static int open_directory(int root, const char *const *components, size_t count)
{
    int directory = fcntl(root, F_DUPFD_CLOEXEC, 3);
    if (directory < 0)
        return -errno;
    for (size_t i = 0; i < count; ++i) {
        int next = openat(directory, components[i], O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
        int result = next < 0 ? -errno : protected_directory(next);
        close(directory);
        if (result < 0) {
            if (next >= 0)
                close(next);
            return result;
        }
        directory = next;
    }
    return directory;
}

static int protected_file(int descriptor, struct statx *observed)
{
    const unsigned fields = STATX_TYPE | STATX_MODE | STATX_UID | STATX_INO | STATX_MNT_ID | STATX_CTIME | STATX_SIZE;
    if (statx(descriptor, "", AT_EMPTY_PATH, fields, observed) < 0)
        return -errno;
    if ((observed->stx_mask & fields) != fields)
        return -EPROTO;
    return S_ISREG(observed->stx_mode) && observed->stx_uid == 0 && (observed->stx_mode & 06022) == 0 ? 0 : -EACCES;
}

static int open_file(struct fc_installation *installation, enum fc_install_file file, int directory, const char *name, int flags)
{
    int descriptor = openat(directory, name, flags | O_NOFOLLOW | O_CLOEXEC);
    if (descriptor < 0)
        return -errno;
    installation->descriptors[file] = descriptor;
    return protected_file(descriptor, &installation->observed[file]);
}

static int read_manifest(int descriptor)
{
    int input = fcntl(descriptor, F_DUPFD_CLOEXEC, 3);
    if (input < 0)
        return -errno;
    FILE *manifest = fdopen(input, "r");
    if (!manifest) {
        int error = errno;
        close(input);
        return -error;
    }

    char line[PATH_MAX + 1];
    size_t total = 0;
    unsigned seen = 0;
    int result = 0;
    while (fgets(line, sizeof(line), manifest)) {
        size_t length = strlen(line);
        total += length;
        if (length == 0 || line[length - 1] != '\n' || total > 1024 * 1024) {
            result = -EPROTO;
            break;
        }
        line[length - 1] = '\0';
        for (size_t i = 0; i < sizeof(package_files) / sizeof(package_files[0]); ++i) {
            if (strcmp(line, package_files[i]) == 0) {
                if (seen & (1u << i))
                    result = -EPROTO;
                seen |= 1u << i;
            }
        }
        if (result < 0)
            break;
    }
    if (result == 0 && (ferror(manifest) || seen != (1u << FC_INSTALL_MANIFEST) - 1))
        result = -EACCES;
    fclose(manifest);
    return result;
}

static int admit_package(struct fc_installation *installation, int root)
{
    const char *const package[] = { "opt", "filecat" };
    int directory = open_directory(root, package, sizeof(package) / sizeof(package[0]));
    if (directory < 0)
        return directory;
    int result = 0;
    for (enum fc_install_file i = FC_INSTALL_EXECUTABLE; i <= FC_INSTALL_MECHANISM && result == 0; ++i) {
        const char *name = strrchr(package_files[i], '/') + 1;
        result = open_file(installation, i, directory, name, O_PATH);
        if (result == 0 && (i == FC_INSTALL_EXECUTABLE || i == FC_INSTALL_MECHANISM) &&
            (installation->observed[i].stx_mode & S_IXUSR) == 0)
            result = -EACCES;
    }
    close(directory);
    return result;
}

static int admit_activation_files(struct fc_installation *installation, int root)
{
    const char *const chains[][4] = {
        { "usr", "share", "dbus-1", "system.d" },
        { "usr", "share", "dbus-1", "system-services" },
        { "usr", "share", "polkit-1", "actions" },
        { "usr", "lib", "systemd", "system" }
    };
    _Static_assert(sizeof(chains) / sizeof(chains[0]) == FC_INSTALL_MANIFEST - FC_INSTALL_BUS_POLICY,
        "Each activation file has a protected parent chain");
    for (enum fc_install_file i = FC_INSTALL_BUS_POLICY; i < FC_INSTALL_MANIFEST; ++i) {
        int directory = open_directory(root, chains[i - FC_INSTALL_BUS_POLICY], 4);
        if (directory < 0)
            return directory;
        const char *name = strrchr(package_files[i], '/') + 1;
        int result = open_file(installation, i, directory, name, O_PATH);
        close(directory);
        if (result < 0)
            return result;
    }
    return 0;
}

static int admit_registration(struct fc_installation *installation, int root)
{
    const char *const registration[] = { "var", "lib", "dpkg", "info" };
    int directory = open_directory(root, registration, sizeof(registration) / sizeof(registration[0]));
    if (directory < 0)
        return directory;
    int result = open_file(installation, FC_INSTALL_MANIFEST, directory, "filecat.list", O_RDONLY | O_NONBLOCK);
    close(directory);
    if (result < 0)
        return result;
    if (installation->observed[FC_INSTALL_MANIFEST].stx_size > 1024 * 1024)
        return -E2BIG;
    return read_manifest(installation->descriptors[FC_INSTALL_MANIFEST]);
}

static int same_file(const struct statx *left, const struct statx *right)
{
    return left->stx_dev_major == right->stx_dev_major && left->stx_dev_minor == right->stx_dev_minor &&
           left->stx_ino == right->stx_ino && left->stx_mnt_id == right->stx_mnt_id &&
           left->stx_ctime.tv_sec == right->stx_ctime.tv_sec && left->stx_ctime.tv_nsec == right->stx_ctime.tv_nsec &&
           left->stx_size == right->stx_size && left->stx_mode == right->stx_mode && left->stx_uid == right->stx_uid;
}

static int unchanged_descriptors(const struct fc_installation *installation)
{
    for (size_t i = 0; i < FC_INSTALL_COUNT; ++i) {
        struct statx current;
        int result = protected_file(installation->descriptors[i], &current);
        if (result < 0)
            return result;
        if (!same_file(&installation->observed[i], &current))
            return -ESTALE;
    }
    return 0;
}

int fc_installation_open(struct fc_installation *installation)
{
    memset(installation, 0, sizeof(*installation));
    for (size_t i = 0; i < FC_INSTALL_COUNT; ++i)
        installation->descriptors[i] = -1;

    int root = open("/", O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (root < 0)
        return -errno;
    int result = protected_directory(root);
    if (result == 0)
        result = admit_package(installation, root);
    if (result == 0)
        result = admit_activation_files(installation, root);
    if (result == 0)
        result = admit_registration(installation, root);
    if (result == 0)
        result = unchanged_descriptors(installation);
    close(root);
    if (result < 0)
        fc_installation_close(installation);
    return result;
}

int fc_installation_current(const struct fc_installation *installation)
{
    struct fc_installation current;
    int result = fc_installation_open(&current);
    if (result < 0)
        return result;
    for (size_t i = 0; i < FC_INSTALL_COUNT; ++i) {
        if (!same_file(&installation->observed[i], &current.observed[i])) {
            result = -ESTALE;
            break;
        }
    }
    fc_installation_close(&current);
    return result;
}

int fc_installation_configured(void)
{
    int root = open("/", O_PATH | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC);
    if (root < 0)
        return -errno;
    int directory = protected_directory(root);
    if (directory == 0) {
        const char *const state[] = { "var", "lib", "filecat-permissions" };
        directory = open_directory(root, state, sizeof(state) / sizeof(state[0]));
    }
    close(root);
    if (directory < 0)
        return directory;
    int marker = openat(directory, "installed", O_PATH | O_NOFOLLOW | O_CLOEXEC);
    int result = marker < 0 ? -errno : 0;
    close(directory);
    if (result == 0) {
        struct statx facts;
        result = protected_file(marker, &facts);
        if (result == 0 && (facts.stx_mode & 0111 || facts.stx_size != 0))
            result = -EACCES;
        close(marker);
    }
    return result;
}

void fc_installation_close(struct fc_installation *installation)
{
    for (size_t i = 0; i < FC_INSTALL_COUNT; ++i) {
        if (installation->descriptors[i] >= 0)
            close(installation->descriptors[i]);
        installation->descriptors[i] = -1;
    }
}
