#ifndef FILECAT_PERMISSIONS_ACCESS_EFFECT_H
#define FILECAT_PERMISSIONS_ACCESS_EFFECT_H

#include "security.h"

/* Return 1 if any Linux discretionary access request gains permission, 0 otherwise. */
int fc_access_widens(const struct fc_security_snapshot *before, const struct fc_security_snapshot *after);

#endif
