#!/usr/bin/env python3
"""Compare raw connected test inventories without treating skips as passes."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def read(path: Path, casefold_substring: str):
    data = path.read_bytes()
    tree = ET.fromstring(data)
    rows = tree.findall('.//t:UnitTestResult', NS)
    counts = tree.find('.//t:Counters', NS).attrib
    if len(rows) != int(counts['total']):
        raise ValueError(f'Raw case count differs from declared total: {path}')
    if any(row.attrib['outcome'] not in ('Passed', 'NotExecuted', 'Failed') for row in rows):
        raise ValueError(f'Unexpected raw case outcome: {path}')
    passed = sum(row.attrib['outcome'] == 'Passed' for row in rows)
    failed = sum(row.attrib['outcome'] == 'Failed' for row in rows)
    if passed != int(counts['passed']) or failed != int(counts['failed']):
        raise ValueError(f'Raw outcomes differ from counters: {path}')

    def name(row):
        value = row.attrib['testName']
        return value.casefold() if casefold_substring and casefold_substring in value else value

    return (Counter(name(row) for row in rows),
            Counter(name(row) for row in rows if row.attrib['outcome'] == 'Passed'),
            dict(total=len(rows), passed=passed, failed=failed,
                 skipped=sum(row.attrib['outcome'] == 'NotExecuted' for row in rows),
                 sha256=hashlib.sha256(data).hexdigest()))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('previous', type=Path)
    parser.add_argument('current', type=Path)
    parser.add_argument('--casefold-substring', default='')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    before, prior_pass, before_counts = read(args.previous, args.casefold_substring)
    after, current_pass, after_counts = read(args.current, args.casefold_substring)
    missing = list((before - after).elements())
    lost = list((prior_pass - current_pass).elements())
    report = dict(previous=before_counts, current=after_counts,
                  added_cases=sum((after - before).values()),
                  missing_prior_cases=missing, lost_prior_passes=lost)
    if args.output:
        args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
    if missing or lost or after_counts['failed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
