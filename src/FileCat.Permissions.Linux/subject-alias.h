#ifndef FILECAT_PERMISSIONS_SUBJECT_ALIAS_H
#define FILECAT_PERMISSIONS_SUBJECT_ALIAS_H

#include "subject.h"

typedef int (*fc_subject_alias_operation)(int directory, const char *alias, void *argument);

/* Invoke a fixed metadata operation with the verified fd directory as a thread-private cwd. */
int fc_subject_with_alias(const struct fc_subject *subject, const struct fc_subject_state *expected,
    fc_subject_alias_operation operation, void *argument);

#endif
