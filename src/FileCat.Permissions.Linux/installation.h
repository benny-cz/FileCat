#ifndef FILECAT_PERMISSIONS_INSTALLATION_H
#define FILECAT_PERMISSIONS_INSTALLATION_H

#include <linux/stat.h>

#define FC_INSTALLED_EXECUTABLE "/opt/filecat/FileCat"
#define FC_INSTALLED_RUNTIME "/opt/filecat/libcoreclr.so"

enum fc_install_file {
    FC_INSTALL_EXECUTABLE,
    FC_INSTALL_RUNTIME,
    FC_INSTALL_CONFIG,
    FC_INSTALL_MECHANISM,
    FC_INSTALL_BUS_POLICY,
    FC_INSTALL_BUS_SERVICE,
    FC_INSTALL_POLKIT_ACTIONS,
    FC_INSTALL_SYSTEMD_UNIT,
    FC_INSTALL_MANIFEST,
    FC_INSTALL_COUNT
};

struct fc_installation {
    int descriptors[FC_INSTALL_COUNT];
    struct statx observed[FC_INSTALL_COUNT];
};

/* On failure each descriptor is closed. On success the caller owns the snapshot. */
int fc_installation_open(struct fc_installation *installation);
int fc_installation_current(const struct fc_installation *installation);
int fc_installation_configured(void);
void fc_installation_close(struct fc_installation *installation);

#endif
