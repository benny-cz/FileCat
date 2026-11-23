#define _GNU_SOURCE
#include "subject-alias.h"

#include <errno.h>
#include <fcntl.h>
#include <pthread.h>
#include <sched.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

struct alias_request {
    int descriptor;
    const struct fc_subject_state *expected;
    fc_subject_alias_operation operation;
    void *argument;
    int result;
};

static int invoke(const struct alias_request *request)
{
    int proc = fc_linux_own_proc_thread();
    if (proc < 0)
        return proc;
    int directory = fc_linux_proc_entry(proc, "fd", O_PATH | O_DIRECTORY);
    close(proc);
    if (directory < 0)
        return directory;

    int result;
    if (unshare(CLONE_FS) < 0 || fchdir(directory) < 0) {
        result = -errno;
    } else {
        char alias[16];
        int length = snprintf(alias, sizeof(alias), "%d", request->descriptor);
        result = length <= 0 || (size_t)length >= sizeof(alias) ? -EINVAL :
            fc_subject_verify_proc_alias(directory, alias, request->expected);
        if (result == 0)
            result = request->operation(directory, alias, request->argument);
    }
    close(directory);
    return result;
}

static void *metadata_worker(void *argument)
{
    struct alias_request *request = argument;
    request->result = invoke(request);
    return NULL;
}

int fc_subject_with_alias(const struct fc_subject *subject, const struct fc_subject_state *expected,
    fc_subject_alias_operation operation, void *argument)
{
    struct alias_request request = {
        .descriptor = subject->pin, .expected = expected, .operation = operation, .argument = argument
    };
    pthread_t worker;
    int result = pthread_create(&worker, NULL, metadata_worker, &request);
    if (result != 0)
        return -result;
    /* Returning without joining would leave the worker with borrowed stack state. */
    if (pthread_join(worker, NULL) != 0)
        abort();
    return request.result;
}
