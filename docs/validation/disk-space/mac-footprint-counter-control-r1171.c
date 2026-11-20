#include <libproc.h>
#include <sys/resource.h>
#include <sys/mman.h>
#include <unistd.h>
#include <stddef.h>
#include <stdint.h>
#include <inttypes.h>
#include <stdio.h>

static int sample(struct rusage_info_v4 *value) {
    return proc_pid_rusage(getpid(), RUSAGE_INFO_V4, (rusage_info_t *)value);
}

int main(void) {
    struct rusage_info_v4 before = {0}, touched = {0}, released = {0};
    const size_t bytes = 64u * 1024u * 1024u;
    int page = getpagesize();
    if (page <= 0 || sample(&before)) return 2;
    volatile unsigned char *memory = mmap(NULL, bytes, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
    if ((void *)memory == MAP_FAILED) return 3;
    for (size_t at = 0; at < bytes; at += (size_t)page) memory[at] = 0xa5;
    if (sample(&touched)) return 4;
    if (munmap((void *)memory, bytes) || sample(&released)) return 5;
    int passed = touched.ri_phys_footprint >= before.ri_phys_footprint + 56u * 1024u * 1024u
        && touched.ri_lifetime_max_phys_footprint >= touched.ri_phys_footprint
        && released.ri_phys_footprint <= before.ri_phys_footprint + 8u * 1024u * 1024u;
    printf("{\"counter\":\"proc_pid_rusage/RUSAGE_INFO_V4 physical footprint\","
           "\"structBytes\":%zu,\"footprintOffset\":%zu,\"lifetimePeakOffset\":%zu,"
           "\"pageBytes\":%d,\"touchedBytes\":%zu,\"before\":%" PRIu64 ",\"touched\":%" PRIu64
           ",\"released\":%" PRIu64 ",\"lifetimePeak\":%" PRIu64 ",\"passed\":%s}\n",
           sizeof(before), offsetof(struct rusage_info_v4, ri_phys_footprint),
           offsetof(struct rusage_info_v4, ri_lifetime_max_phys_footprint), page, bytes,
           before.ri_phys_footprint, touched.ri_phys_footprint, released.ri_phys_footprint,
           touched.ri_lifetime_max_phys_footprint, passed ? "true" : "false");
    return passed ? 0 : 1;
}
