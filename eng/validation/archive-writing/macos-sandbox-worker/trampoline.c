// macOS App Sandbox worker spike: a launcher signed with only the App Sandbox entitlement and an embedded Info.plist
// (the sandbox refuses a bare executable whose signature names no bundle identifier). libsystem_secinit applies the
// sandbox before main; the launcher then replaces itself with the bundle's own .NET executable, which keeps the sandbox
// across exec. Validation tooling only: it executes nothing but the fixed sibling probe.
#include <errno.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <mach/mach.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

extern char **environ;

int main(int argc, char **argv)
{
    (void)argc;
    char self[PATH_MAX], real[PATH_MAX], target[PATH_MAX];
    uint32_t size = sizeof self;
    if (_NSGetExecutablePath(self, &size) != 0 || realpath(self, real) == NULL) return 71;
    char *slash = strrchr(real, '/');
    if (slash == NULL) return 72;
    *slash = '\0';
    if (snprintf(target, sizeof target, "%s/SandboxProbe", real) >= (int)sizeof target) return 73;
    argv[0] = target;
#ifdef FILECAT_NULL_BOOTSTRAP
    // After the sandbox is in place, give up the bootstrap port: the replacement image can look up no mach service.
    if (task_set_special_port(mach_task_self(), TASK_BOOTSTRAP_PORT, MACH_PORT_NULL) != KERN_SUCCESS) return 75;
#endif
#ifdef FILECAT_VARIANT_TAG
    static volatile const char tag[] = "variant build with different bytes";
    if (tag[0] == 0) return 76;
#endif
    execve(target, argv, environ);
    fprintf(stderr, "exec failed: %s\n", strerror(errno));
    return 74;
}
