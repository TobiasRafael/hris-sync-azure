"""Writes a Markdown test summary (for $GITHUB_STEP_SUMMARY) from the TRX files under a directory.

Usage: python3 trx_summary.py <results-dir>
Exits 0 even when tests failed: the test step itself fails the job.
"""

import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
MAX_FAILURES = 20


def main(results_dir: str) -> None:
    files = sorted(pathlib.Path(results_dir).rglob("*.trx"))
    if not files:
        print("## Tests\n\n:warning: No TRX files found.")
        return

    rows, failures = [], []
    totals = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}

    for trx in files:
        root = ET.parse(trx).getroot()
        counters = root.find("t:ResultSummary/t:Counters", NS)
        total = int(counters.get("total", 0))
        passed = int(counters.get("passed", 0))
        failed = int(counters.get("failed", 0)) + int(counters.get("error", 0)) + int(counters.get("timeout", 0))
        skipped = total - passed - failed

        if total == 0:
            # e.g. an assembly whose tests are all excluded by --filter (the E2E project).
            continue

        code_base = next((m.get("codeBase") for m in root.iterfind("t:TestDefinitions/t:UnitTest/t:TestMethod", NS)), "")
        name = pathlib.PurePath(code_base.replace("\\", "/")).stem if code_base else trx.stem
        rows.append((name, total, passed, failed, skipped))
        for key, value in zip(totals, (total, passed, failed, skipped)):
            totals[key] += value

        for result in root.iterfind("t:Results/t:UnitTestResult", NS):
            if result.get("outcome") == "Failed":
                message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
                failures.append((result.get("testName"), message.strip().splitlines()[0] if message.strip() else ""))

    icon = ":x:" if totals["failed"] else ":white_check_mark:"
    print(f"## {icon} Tests: {totals['passed']} passed, {totals['failed']} failed, "
          f"{totals['skipped']} skipped ({totals['total']} total)\n")
    print("| Assembly | Total | Passed | Failed | Skipped |")
    print("|---|---:|---:|---:|---:|")
    for name, total, passed, failed, skipped in rows:
        print(f"| {name} | {total} | {passed} | {failed} | {skipped} |")

    if failures:
        print("\n### Failed tests\n")
        for test, message in failures[:MAX_FAILURES]:
            print(f"- `{test}`: {message}")
        if len(failures) > MAX_FAILURES:
            print(f"- ...and {len(failures) - MAX_FAILURES} more (see the test-results artifact)")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "TestResults")
