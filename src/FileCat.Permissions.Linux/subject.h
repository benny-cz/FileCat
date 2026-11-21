#ifndef FILECAT_PERMISSIONS_SUBJECT_H
#define FILECAT_PERMISSIONS_SUBJECT_H

#include <linux/stat.h>
#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>

/* Match the bounds of the ordinary Linux inspector. */
enum { FC_SUBJECT_PATH_BYTES = 4096, FC_SUBJECT_PATH_DEPTH = 256 };

struct fc_subject_identity {
    uint64_t mount;
    uint64_t inode;
    uint32_t device_major;
    uint32_t device_minor;
    uint16_t kind;
};

struct fc_subject_state {
    struct statx facts;
    uint64_t mount_attributes;
};

struct fc_subject {
    int pin;
    char path[FC_SUBJECT_PATH_BYTES];
    size_t depth;
    struct fc_subject_identity components[FC_SUBJECT_PATH_DEPTH + 1];
};

#define FC_SUBJECT_INIT { .pin = -1 }

/* The caller has verified the peer's user, process, mount and root-directory context.
   The received descriptor is borrowed. On success the subject owns a duplicate. */
int fc_subject_bind(struct fc_subject *subject, int received, const char *path,
    const struct fc_subject_identity *reviewed, struct fc_subject_state *state);
int fc_subject_observe(const struct fc_subject *subject, struct fc_subject_state *state);
int fc_subject_state_matches(const struct fc_subject_state *left, const struct fc_subject_state *right);
/* Compute strictly_restrictive from the mechanism's validated change, before calling this function. */
int fc_subject_check_write(const struct fc_subject *subject, const struct fc_subject_state *reviewed,
    uid_t requester, int strictly_restrictive);
void fc_subject_close(struct fc_subject *subject);

/* The metadata worker uses these with fixed procfs names and its live decimal descriptor name. */
int fc_linux_own_proc_thread(void);
int fc_linux_proc_entry(int proc, const char *name, int flags);
int fc_subject_verify_proc_alias(int directory, const char *name, const struct fc_subject_state *expected);

#endif
