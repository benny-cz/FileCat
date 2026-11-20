"""Compact read-only progress; observations never establish process idleness or test qualification."""
from pathlib import Path
from datetime import datetime, timezone
import argparse, json, re


def observe(task, folder, now=None):
    if not re.fullmatch(r'awe-storage-v[1-9][0-9]*', folder):
        raise ValueError('Expected one archive storage cohort name.')
    task = Path(task).resolve()
    out = task / folder
    now = now or datetime.now(timezone.utc)
    warnings = []

    def document(path):
        try:
            return json.loads(path.read_text(encoding='utf-8-sig'))
        except FileNotFoundError:
            return None
        except (OSError, ValueError) as error:
            warnings.append(dict(file=path.name, error=type(error).__name__))
            return None

    # A runner may be appending the last line. Keep complete entries and report a partial line.
    events = []
    try:
        lines = (out / 'execution-timings.jsonl').read_text(encoding='utf-8').splitlines()
    except FileNotFoundError:
        lines = []
    except OSError as error:
        lines = []
        warnings.append(dict(file='execution-timings.jsonl', error=type(error).__name__))
    for line in lines:
        try:
            event = json.loads(line)
            if not isinstance(event, dict) or not all(isinstance(event.get(key), str) for key in ('operation', 'name', 'event', 'utc')):
                raise ValueError('Incomplete timing event.')
            events.append(event)
        except ValueError:
            warnings.append(dict(file='execution-timings.jsonl', error='IncompleteOrInvalidEvent'))
    finished = {row['operation'] for row in events if row['event'] in ('returned', 'failed')}
    active = []
    for row in events:
        if row['event'] != 'started' or row['operation'] in finished:
            continue
        value = dict(name=row['name'], started_utc=row['utc'])
        try:
            value['elapsed_seconds'] = round(max(0, (now - datetime.fromisoformat(row['utc'])).total_seconds()), 1)
        except (TypeError, ValueError):
            warnings.append(dict(file='execution-timings.jsonl', error='InvalidStartedTime'))
        active.append(value)
    passed = document(out / 'passed-gates.json')
    counts = {}
    if isinstance(passed, dict):
        try:
            counts = {name: {key: int(row[key]) for key in ('passed', 'failed', 'notExecuted', 'total')}
                      for name, row in passed.items()}
        except (KeyError, TypeError, ValueError):
            warnings.append(dict(file='passed-gates.json', error='InvalidCounters'))
    attempts = document(out / 'full-regression-attempts.json')
    evidence = document(task / (folder + '-evidence.tar.json'))
    complete = document(out / 'tests-complete.json')
    census = document(out / 'final-census.json')
    return dict(folder=folder, observed_utc=now.isoformat(), output_present=out.is_dir(),
                active_commands_unconfirmed=active, passed_gate_counts=counts,
                full_regression_attempts=attempts, tests_complete_record_present=complete is not None,
                final_census=census, sealed_transport_unverified=evidence, warnings=warnings,
                qualification='Not established by this read-only observation; verify the sealed evidence.',
                idleness='Not checked; use the existing machine/process sharing check before testing.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('task', type=Path)
    parser.add_argument('folder')
    args = parser.parse_args()
    print(json.dumps(observe(args.task, args.folder), indent=2))
