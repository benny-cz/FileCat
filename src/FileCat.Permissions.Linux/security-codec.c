#include "security.h"

#include <errno.h>
#include <linux/capability.h>
#include <linux/posix_acl.h>
#include <linux/posix_acl_xattr.h>

static uint16_t word(const unsigned char *bytes)
{
    return (uint16_t)((unsigned)bytes[0] | (unsigned)bytes[1] << 8);
}

static uint32_t number(const unsigned char *bytes)
{
    return (uint32_t)word(bytes) | (uint32_t)word(bytes + 2) << 16;
}

static struct fc_acl_entry decode_entry(const unsigned char *bytes)
{
    return (struct fc_acl_entry) { word(bytes), word(bytes + 2), number(bytes + 4) };
}

static int known_tag(uint16_t tag)
{
    return tag == ACL_USER_OBJ || tag == ACL_USER || tag == ACL_GROUP_OBJ ||
        tag == ACL_GROUP || tag == ACL_MASK || tag == ACL_OTHER;
}

void fc_acl_decode(struct fc_acl_evidence *acl, size_t length)
{
    acl->status = FC_SECURITY_INCOMPLETE;
    acl->error = EBADMSG;
    acl->length = 0;
    acl->entries = 0;
    if (length > sizeof(acl->bytes)) {
        acl->error = E2BIG;
        return;
    }
    acl->length = length;
    if (length < 4)
        return;
    if (number(acl->bytes) != POSIX_ACL_XATTR_VERSION) {
        acl->status = FC_SECURITY_OPAQUE;
        acl->error = EOPNOTSUPP;
        return;
    }
    if ((length - 4) % 8 != 0 || (length - 4) / 8 < 3)
        return;

    uint16_t previous = 0;
    uint32_t previous_id = 0;
    int named = 0, group = 0, mask = 0;
    for (size_t offset = 4; offset < length; offset += 8) {
        struct fc_acl_entry entry = decode_entry(acl->bytes + offset);
        if (!known_tag(entry.tag)) {
            acl->status = FC_SECURITY_OPAQUE;
            acl->error = EOPNOTSUPP;
            return;
        }
        int qualified = entry.tag == ACL_USER || entry.tag == ACL_GROUP;
        if (entry.rights > 7 || (qualified ? entry.id == UINT32_MAX : entry.id != UINT32_MAX) ||
            (offset == 4 && entry.tag != ACL_USER_OBJ) || entry.tag < previous ||
            (entry.tag == previous && (!qualified || entry.id <= previous_id)))
            return;
        named |= qualified;
        group |= entry.tag == ACL_GROUP_OBJ;
        mask |= entry.tag == ACL_MASK;
        previous = entry.tag;
        previous_id = entry.id;
    }
    if (!group || previous != ACL_OTHER || (named && !mask))
        return;
    acl->status = FC_SECURITY_PRESENT;
    acl->error = 0;
    acl->entries = (length - 4) / 8;
}

int fc_acl_entry_at(const struct fc_acl_evidence *acl, size_t index, struct fc_acl_entry *entry)
{
    if (acl->status != FC_SECURITY_PRESENT || index >= acl->entries || index >= FC_ACL_MAX_ENTRIES)
        return -EINVAL;
    *entry = decode_entry(acl->bytes + 4 + 8 * index);
    return 0;
}

void fc_capabilities_decode(struct fc_capability_evidence *capabilities, size_t length)
{
    capabilities->status = FC_SECURITY_INCOMPLETE;
    capabilities->error = EBADMSG;
    capabilities->length = 0;
    capabilities->version = 0;
    capabilities->effective = 0;
    capabilities->permitted = 0;
    capabilities->inheritable = 0;
    capabilities->root_id = 0;
    if (length > sizeof(capabilities->bytes)) {
        capabilities->error = E2BIG;
        return;
    }
    capabilities->length = length;
    if (length < 4)
        return;

    const unsigned char *bytes = capabilities->bytes;
    uint32_t header = number(bytes);
    uint32_t revision = header & VFS_CAP_REVISION_MASK;
    size_t expected;
    switch (revision) {
    case VFS_CAP_REVISION_1: expected = XATTR_CAPS_SZ_1; break;
    case VFS_CAP_REVISION_2: expected = XATTR_CAPS_SZ_2; break;
    case VFS_CAP_REVISION_3: expected = XATTR_CAPS_SZ_3; break;
    default: expected = 0; break;
    }
    if (expected == 0 || (header & ~(uint32_t)(VFS_CAP_REVISION_MASK | VFS_CAP_FLAGS_EFFECTIVE))) {
        capabilities->status = FC_SECURITY_OPAQUE;
        capabilities->error = EOPNOTSUPP;
        return;
    }
    if (length != expected || (revision == VFS_CAP_REVISION_3 && number(bytes + 20) == UINT32_MAX))
        return;
    capabilities->permitted = number(bytes + 4);
    capabilities->inheritable = number(bytes + 8);
    if (revision != VFS_CAP_REVISION_1) {
        capabilities->permitted |= (uint64_t)number(bytes + 12) << 32;
        capabilities->inheritable |= (uint64_t)number(bytes + 16) << 32;
    }
    if (revision == VFS_CAP_REVISION_3)
        capabilities->root_id = number(bytes + 20);
    capabilities->version = revision >> VFS_CAP_REVISION_SHIFT;
    capabilities->effective = (header & VFS_CAP_FLAGS_EFFECTIVE) != 0;
    capabilities->status = FC_SECURITY_PRESENT;
    capabilities->error = 0;
}
