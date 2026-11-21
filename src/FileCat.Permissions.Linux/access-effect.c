#include "access-effect.h"

#include <errno.h>
#include <linux/posix_acl.h>
#include <stdlib.h>
#include <sys/stat.h>

/* Bit N denotes whether that entry grants request N, for each nonempty rwx combination. */
enum { all_requests = 0xfe };

struct rule {
    uint32_t id;
    unsigned requests;
};

struct access_rules {
    uint32_t owner;
    unsigned owner_requests, other_requests;
    struct rule *users, *groups;
    size_t user_count, group_count;
};

static unsigned requests_for(unsigned rights)
{
    unsigned requests = 0;
    for (unsigned request = 1; request <= 7; ++request) {
        if ((rights & request) == request)
            requests |= 1u << request;
    }
    return requests;
}

static int compare_ids(const void *left, const void *right)
{
    const struct rule *a = left, *b = right;
    return (a->id > b->id) - (a->id < b->id);
}

static void finish_groups(struct access_rules *access, unsigned mask)
{
    qsort(access->groups, access->group_count, sizeof(*access->groups), compare_ids);
    size_t unique = 0;
    for (size_t i = 0; i < access->group_count; ++i) {
        struct rule group = access->groups[i];
        group.requests = requests_for(group.requests & mask);
        if (unique && access->groups[unique - 1].id == group.id) {
            /* The owning and a named group can share an ID. Linux tests each entry separately. */
            access->groups[unique - 1].requests |= group.requests;
        } else {
            access->groups[unique++] = group;
        }
    }
    access->group_count = unique;
}

static int decode_access(const struct fc_security_snapshot *snapshot, struct access_rules *access)
{
    const struct fc_acl_evidence *acl = &snapshot->access;
    if (!(snapshot->sections & FC_SECURITY_ACCESS) ||
        (acl->status != FC_SECURITY_PRESENT && acl->status != FC_SECURITY_ABSENT))
        return -EOPNOTSUPP;
    if (acl->entries > FC_ACL_MAX_ENTRIES || (acl->status == FC_SECURITY_PRESENT && acl->entries < 3))
        return -EBADMSG;

    const struct statx *facts = &snapshot->subject.facts;
    if (facts->stx_uid == UINT32_MAX || facts->stx_gid == UINT32_MAX)
        return -EBADMSG;
    /* The Linux VFS goes directly to BSD mode when the group-class bits are zero. */
    size_t entry_count = (facts->stx_mode & S_IRWXG) ? acl->entries : 0;
    size_t capacity = entry_count + 1;
    access->users = calloc(2 * capacity, sizeof(*access->users));
    if (!access->users)
        return -ENOMEM;
    access->groups = access->users + capacity;
    access->owner = facts->stx_uid;
    access->owner_requests = requests_for((facts->stx_mode >> 6) & 7u);
    access->other_requests = requests_for(facts->stx_mode & 7u);
    access->groups[access->group_count++] = (struct rule) { facts->stx_gid, (facts->stx_mode >> 3) & 7u };

    unsigned mask = 7;
    for (size_t i = 0; i < entry_count; ++i) {
        struct fc_acl_entry entry;
        int result = fc_acl_entry_at(acl, i, &entry);
        if (result < 0)
            return result;
        switch (entry.tag) {
        case ACL_USER_OBJ: access->owner_requests = requests_for(entry.rights); break;
        case ACL_USER: access->users[access->user_count++] = (struct rule) { entry.id, entry.rights }; break;
        case ACL_GROUP_OBJ: access->groups[0].requests = entry.rights; break;
        case ACL_GROUP: access->groups[access->group_count++] = (struct rule) { entry.id, entry.rights }; break;
        case ACL_MASK: mask = entry.rights; break;
        case ACL_OTHER: access->other_requests = requests_for(entry.rights); break;
        default: return -EBADMSG;
        }
    }
    for (size_t i = 0; i < access->user_count; ++i)
        access->users[i].requests = requests_for(access->users[i].requests & mask);
    finish_groups(access, mask);
    return 0;
}

struct group_effect {
    unsigned before_can_deny, after_can_grant, additional;
};

static struct group_effect compare_groups(const struct access_rules *before, const struct access_rules *after)
{
    unsigned blocked_by_membership = 0;
    for (size_t i = 0; i < before->group_count; ++i)
        blocked_by_membership |= all_requests & ~before->groups[i].requests;

    struct group_effect effect = {
        .before_can_deny = (all_requests & ~before->other_requests) | blocked_by_membership,
        .after_can_grant = after->other_requests,
        .additional = after->other_requests & ~before->other_requests
    };
    size_t left = 0, right = 0;
    while (left < before->group_count || right < after->group_count) {
        uint32_t left_id = left < before->group_count ? before->groups[left].id : UINT32_MAX;
        uint32_t right_id = right < after->group_count ? after->groups[right].id : UINT32_MAX;
        int in_before = left_id <= right_id;
        int in_after = right_id <= left_id;
        unsigned old = in_before ? before->groups[left++].requests : 0;
        unsigned new = in_after ? after->groups[right++].requests : 0;
        effect.after_can_grant |= new;

        /* Choose a granting new group. It must not grant the same request in the old rules.
           If it wasn't listed before, the old rules must still be able to deny the request. */
        effect.additional |= new & ~old & (in_before ? all_requests : effect.before_can_deny);
        /* Membership in a removed, non-granting group used to prevent falling through to Other. */
        if (in_before && !in_after)
            effect.additional |= after->other_requests & ~old;
    }
    return effect;
}

static int user_requests(const struct access_rules *acl, uint32_t user, unsigned *requests)
{
    if (user == acl->owner) {
        *requests = acl->owner_requests;
        return 1;
    }
    const struct rule key = { .id = user };
    const struct rule *found = bsearch(&key, acl->users, acl->user_count, sizeof(key), compare_ids);
    if (!found)
        return 0;
    *requests = found->requests;
    return 1;
}

static int user_widens(uint32_t user, const struct access_rules *before,
    const struct access_rules *after, struct group_effect group)
{
    unsigned old = 0, new = 0;
    int before_direct = user_requests(before, user, &old);
    int after_direct = user_requests(after, user, &new);
    if (before_direct && after_direct)
        return (new & ~old) != 0;
    if (before_direct)
        return (group.after_can_grant & ~old) != 0;
    if (after_direct)
        return (new & group.before_can_deny) != 0;
    return group.additional != 0;
}

int fc_access_widens(const struct fc_security_snapshot *before, const struct fc_security_snapshot *after)
{
    struct access_rules old = { 0 }, new = { 0 };
    int result = decode_access(before, &old);
    if (result == 0)
        result = decode_access(after, &new);
    if (result == 0) {
        struct group_effect groups = compare_groups(&old, &new);
        /* Every remaining UID follows the shared group/other policy. */
        result = groups.additional || user_widens(old.owner, &old, &new, groups) ||
            user_widens(new.owner, &old, &new, groups);
        for (size_t i = 0; i < old.user_count && !result; ++i)
            result = user_widens(old.users[i].id, &old, &new, groups);
        for (size_t i = 0; i < new.user_count && !result; ++i)
            result = user_widens(new.users[i].id, &old, &new, groups);
    }
    free(new.users);
    free(old.users);
    return result;
}
