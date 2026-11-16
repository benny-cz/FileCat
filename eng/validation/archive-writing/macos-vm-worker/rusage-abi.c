/* Owned SDK ABI check; no privileged call or resource-policy claim. */
#include <libproc.h>
#include <stddef.h>
#include <stdio.h>
#include <sys/resource.h>
#include <unistd.h>
int main(void)
{
    struct rusage_info_v4 info = {0};
    if (proc_pid_rusage(getpid(), RUSAGE_INFO_V4, (rusage_info_t *)&info) != 0) return 65;
    printf("{\"size\":%zu,\"resident_offset\":%zu,\"footprint_offset\":%zu,\"start_offset\":%zu,\"lifetime_offset\":%zu,\"self_footprint\":%llu}\n",
        sizeof(info), offsetof(struct rusage_info_v4, ri_resident_size), offsetof(struct rusage_info_v4, ri_phys_footprint),
        offsetof(struct rusage_info_v4, ri_proc_start_abstime), offsetof(struct rusage_info_v4, ri_lifetime_max_phys_footprint),
        (unsigned long long)info.ri_phys_footprint);
    return 0;
}
