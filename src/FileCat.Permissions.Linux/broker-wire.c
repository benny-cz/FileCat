#define _GNU_SOURCE
#include "broker-wire.h"

#include <errno.h>
#include <string.h>
#include <sys/stat.h>

static int end_of_message(sd_bus_message *message)
{
    return sd_bus_message_at_end(message, 1) > 0 ? 0 : -EBADMSG;
}

int fc_wire_read_version(sd_bus_message *message)
{
    uint32_t version;
    if (sd_bus_message_read(message, "u", &version) != 1)
        return -EBADMSG;
    return version == FC_BROKER_VERSION ? 0 : -EPROTONOSUPPORT;
}

static int read_fixed(sd_bus_message *message, void *destination, size_t requested)
{
    const void *bytes;
    size_t size;
    if (sd_bus_message_read_array(message, 'y', &bytes, &size) != 1 || size != requested)
        return -EBADMSG;
    memcpy(destination, bytes, size);
    return 0;
}

int fc_wire_read_token(sd_bus_message *message, unsigned char token[FC_BROKER_TOKEN_BYTES])
{
    return read_fixed(message, token, FC_BROKER_TOKEN_BYTES);
}

static int read_change(sd_bus_message *message, struct fc_wire_operation *operation)
{
    uint32_t kind;
    if (sd_bus_message_read(message, "u", &kind) != 1)
        return -EBADMSG;
    struct fc_change *change = &operation->change;
    int result;
    /* Wire verb numbers are stable independently of the C enum. */
    switch (kind) {
    case 1:
        change->kind = FC_CHANGE_MODE;
        result = sd_bus_message_read(message, "v", "u", &change->value.mode);
        break;
    case 2:
        change->kind = FC_CHANGE_OWNERSHIP;
        result = sd_bus_message_read(message, "v", "(uu)", &change->value.ownership.uid, &change->value.ownership.gid);
        break;
    case 3:
    case 4: {
        change->kind = kind == 3 ? FC_CHANGE_ACCESS_ACL : FC_CHANGE_DEFAULT_ACL;
        const void *bytes;
        size_t size;
        if (sd_bus_message_enter_container(message, 'v', "ay") != 1 ||
            sd_bus_message_read_array(message, 'y', &bytes, &size) != 1)
            return -EBADMSG;
        if (size > sizeof(operation->acl))
            return -E2BIG;
        if (size)
            memcpy(operation->acl, bytes, size);
        change->value.acl.bytes = operation->acl;
        change->value.acl.length = size;
        result = sd_bus_message_exit_container(message);
        break;
    }
    case 5:
        change->kind = FC_CHANGE_FLAGS;
        result = sd_bus_message_read(message, "v", "(uu)", &change->value.flags.set, &change->value.flags.clear);
        break;
    default:
        return -EOPNOTSUPP;
    }
    return result == 1 ? end_of_message(message) : -EBADMSG;
}

int fc_wire_read_operation(sd_bus_message *message, int apply, struct fc_wire_operation *operation)
{
    const void *path;
    size_t size;
    if (sd_bus_message_read_array(message, 'y', &path, &size) != 1 || size == 0 ||
        size >= sizeof(operation->path) || memchr(path, 0, size) || ((const char *)path)[0] != '/')
        return -EBADMSG;
    memcpy(operation->path, path, size);
    operation->path[size] = 0;

    struct fc_subject_identity *identity = &operation->identity;
    if (sd_bus_message_read(message, "h(ttuuq)u", &operation->descriptor, &identity->mount, &identity->inode,
            &identity->device_major, &identity->device_minor, &identity->kind, &operation->sections) != 1)
        return -EBADMSG;
    if (!identity->mount || !identity->inode || (identity->kind != S_IFREG && identity->kind != S_IFDIR) ||
        operation->sections == 0 || (operation->sections & ~(unsigned)FC_SECURITY_ALL) != 0)
        return -EINVAL;
    if (!apply)
        return end_of_message(message);
    int result = read_fixed(message, operation->revision, sizeof(operation->revision));
    return result < 0 ? result : read_change(message, operation);
}

static int append_attribute(sd_bus_message *reply, enum fc_security_status status, int error,
    const unsigned char *bytes, size_t length)
{
    int result = sd_bus_message_open_container(reply, 'r', "uiay");
    if (result >= 0)
        result = sd_bus_message_append(reply, "ui", (uint32_t)status, (int32_t)error);
    if (result >= 0)
        result = sd_bus_message_append_array(reply, 'y', bytes, length);
    if (result >= 0)
        result = sd_bus_message_close_container(reply);
    return result;
}

static int append_snapshot(sd_bus_message *reply, const struct fc_security_snapshot *observed)
{
    int result = sd_bus_message_open_container(reply, 'r', FC_WIRE_SNAPSHOT_FIELDS);
    const struct statx *facts = &observed->subject.facts;
    if (result >= 0)
        result = sd_bus_message_append(reply, "u(ttuuq)(uuuuttttxuxu)", (uint32_t)observed->sections,
            (uint64_t)facts->stx_mnt_id, (uint64_t)facts->stx_ino, facts->stx_dev_major, facts->stx_dev_minor,
            facts->stx_mode & S_IFMT, (uint32_t)facts->stx_mode, facts->stx_uid, facts->stx_gid, facts->stx_nlink,
            (uint64_t)facts->stx_size, (uint64_t)facts->stx_attributes, (uint64_t)facts->stx_attributes_mask,
            observed->subject.mount_attributes, (int64_t)facts->stx_ctime.tv_sec, facts->stx_ctime.tv_nsec,
            (int64_t)facts->stx_mtime.tv_sec, facts->stx_mtime.tv_nsec);
    if (result >= 0)
        result = sd_bus_message_append_array(reply, 'y', observed->revision, sizeof(observed->revision));
    if (result >= 0)
        result = append_attribute(reply, observed->access.status, observed->access.error,
            observed->access.bytes, observed->access.length);
    if (result >= 0)
        result = append_attribute(reply, observed->defaults.status, observed->defaults.error,
            observed->defaults.bytes, observed->defaults.length);
    if (result >= 0)
        result = append_attribute(reply, observed->capabilities.status, observed->capabilities.error,
            observed->capabilities.bytes, observed->capabilities.length);
    if (result >= 0)
        result = sd_bus_message_append(reply, "(uiuu)", (uint32_t)observed->flags.status, (int32_t)observed->flags.error,
            observed->flags.known, observed->flags.bits);
    if (result >= 0)
        result = sd_bus_message_close_container(reply);
    return result;
}

int fc_wire_reply_begin(sd_bus_message *call, const unsigned char token[FC_BROKER_TOKEN_BYTES], int endpoint)
{
    sd_bus_message *reply = NULL;
    int result = sd_bus_message_new_method_return(call, &reply);
    if (result >= 0)
        result = sd_bus_message_append_array(reply, 'y', token, FC_BROKER_TOKEN_BYTES);
    if (result >= 0)
        result = sd_bus_message_append(reply, "h", endpoint);
    if (result >= 0)
        result = sd_bus_send(NULL, reply, NULL);
    sd_bus_message_unref(reply);
    return result;
}

int fc_wire_reply_inspect(sd_bus_message *call, const struct fc_security_snapshot *observed)
{
    sd_bus_message *reply = NULL;
    int result = sd_bus_message_new_method_return(call, &reply);
    if (result >= 0)
        result = append_snapshot(reply, observed);
    if (result >= 0)
        result = sd_bus_send(NULL, reply, NULL);
    sd_bus_message_unref(reply);
    return result;
}

int fc_wire_reply_apply(sd_bus_message *call, const struct fc_write_result *result)
{
    sd_bus_message *reply = NULL;
    int status = sd_bus_message_new_method_return(call, &reply);
    if (status >= 0)
        status = sd_bus_message_append(reply, "uii", (uint32_t)result->state, (int32_t)result->operation_error,
            (int32_t)result->verification_error);
    if (status >= 0)
        status = sd_bus_message_open_container(reply, 'a', FC_WIRE_SNAPSHOT);
    if (status >= 0 && result->has_observation)
        status = append_snapshot(reply, &result->observed);
    if (status >= 0)
        status = sd_bus_message_close_container(reply);
    if (status >= 0)
        status = sd_bus_send(NULL, reply, NULL);
    sd_bus_message_unref(reply);
    return status;
}
