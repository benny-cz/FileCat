"""Exact baseline-case migrations for the schema-3 edit-store change; no broad exclusions."""
from collections import Counter


def migrations():
    rows=[]
    prefix='FileCat.Core.Tests.'
    for value in ('False','True'):
        rows.append(dict(previous=prefix+'EditSessionTests.Reload_ignores_a_session_record_larger_than_the_bounded_metadata_budget(linked: '+value+')',
            current=prefix+'EditSessionTests.Reload_keeps_an_oversized_session_visible_for_readonly_recovery(linked: '+value+')',
            reason='Oversized records must remain visible for read-only recovery; the old ignore contract was replaced.',commit='c68ec836'))
        rows.append(dict(previous=prefix+'EditSessionVersionTests.Version_one_archive_and_remote_records_remain_readable_without_rewriting(remote: '+value+')',
            current=prefix+'EditSessionVersionTests.Legacy_archive_and_remote_records_remain_readable_without_rewriting(version: 1, remote: '+value+')',
            reason='The same unchanged-byte version-1 checks are now parameterized alongside version 2.',commit='c68ec836'))
    rows.append(dict(previous=prefix+'EditSessionVersionTests.Future_versions_remain_visible_and_cannot_be_rewritten_or_discarded(version: 3)',
        current=prefix+'EditSessionVersionTests.Future_versions_remain_visible_and_cannot_be_rewritten_or_discarded(version: 4)',
        reason='Schema 3 is now implemented; the first unknown future schema is 4.',commit='c68ec836'))
    return rows


def review(old,current,expected_new):
    before=Counter(row.get('testName') for row in old);after=Counter(row.get('testName') for row in current)
    changes=migrations()
    assert all(before[row['previous']]==1 and after[row['current']]==1 for row in changes), 'A declared case migration is missing or ambiguous.'
    renamed={row['previous']:row['current'] for row in changes}
    names=lambda cases,passed=False:Counter(renamed.get(row.get('testName'),row.get('testName')) for row in cases if not passed or row.get('outcome')=='Passed')
    actual=lambda cases,passed=False:Counter(row.get('testName') for row in cases if not passed or row.get('outcome')=='Passed')
    return dict(previous=len(old),current=len(current),expected_new_cases=expected_new,name_migrations=changes,
        missing_prior_cases=sorted((names(old)-actual(current)).elements()),
        lost_prior_passes=sorted((names(old,True)-actual(current,True)).elements()))


def require_complete(inventory):
    assert not inventory['missing_prior_cases'] and not inventory['lost_prior_passes'] and \
        inventory['current']==inventory['previous']+inventory['expected_new_cases'],inventory
