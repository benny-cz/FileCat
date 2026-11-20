/* FileCat-owned API probe. No Apple implementation source is incorporated. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if defined(__APPLE__)
#include <unistd.h>
#include <mach/mach.h>
#include <mach/task.h>
#include <mach/task_info.h>

static unsigned long long footprint(void)
{
    task_vm_info_data_t info = {0};
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    kern_return_t result = task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count);
    return result == KERN_SUCCESS ? (unsigned long long)info.phys_footprint : 0;
}

int main(int argc, char **argv)
{
    if (argc != 2 || (strcmp(argv[1], "control") && strcmp(argv[1], "limit"))) return 64;
    int limited = !strcmp(argv[1], "limit"), old_limit = -999;
    kern_return_t status = limited ? task_set_phys_footprint_limit(mach_task_self(), 64, &old_limit) : KERN_SUCCESS;
    printf("{\"phase\":\"setup\",\"pid\":%d,\"uid\":%u,\"limited\":%s,\"requested_mb\":%d,\"status\":%d,\"old_mb\":%d,\"footprint_bytes\":%llu}\n",
        getpid(), (unsigned)getuid(), limited ? "true" : "false", limited ? 64 : 0, status, old_limit, footprint());
    fflush(stdout);
    if (status != KERN_SUCCESS) return 0; /* Refusal is evidence, never an enforcement pass. */
    const size_t chunk = 8u * 1024u * 1024u;
    void *blocks[12] = {0};
    unsigned long long random = 0x36a1d47c59e2b083ULL;
    for (unsigned i = 0; i < 12; i++) {
        blocks[i] = malloc(chunk);
        if (!blocks[i]) {
            printf("{\"phase\":\"allocation-refused\",\"touched_bytes\":%zu,\"footprint_bytes\":%llu}\n", i * chunk, footprint());
            fflush(stdout);
            for (unsigned j = 0; j < i; j++) free(blocks[j]);
            return 0;
        }
        volatile unsigned long long *data = blocks[i];
        for (size_t j = 0; j < chunk / sizeof(*data); j++) {
            random ^= random << 13; random ^= random >> 7; random ^= random << 17;
            data[j] = random;
        }
        printf("{\"phase\":\"touched\",\"touched_bytes\":%zu,\"footprint_bytes\":%llu}\n", (i + 1) * chunk, footprint());
        fflush(stdout);
    }
    printf("{\"phase\":\"completed\",\"touched_bytes\":%zu,\"footprint_bytes\":%llu}\n", 12 * chunk, footprint());
    fflush(stdout);
    for (unsigned i = 0; i < 12; i++) free(blocks[i]);
    return 0;
}
#else
int main(void) { fputs("Darwin-only validation probe.\n", stderr); return 77; }
#endif
