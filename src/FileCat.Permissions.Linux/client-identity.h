#ifndef FILECAT_PERMISSIONS_CLIENT_IDENTITY_H
#define FILECAT_PERMISSIONS_CLIENT_IDENTITY_H

#include "peer.h"
#include "installation.h"

/* The mechanism supplies the executable descriptor and exact path from its protected package. */
int fc_client_identity(const struct fc_peer *peer, int executable, const char *path);
int fc_client_installed_identity(const struct fc_peer *peer, const struct fc_installation *installation);

#endif
