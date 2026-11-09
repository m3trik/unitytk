# !/usr/bin/python
# coding=utf-8
"""Process-wide isolation for a bare ``pytest`` run of this suite.

``pyproject.toml`` configures pytest, which loads this file and never
``run_tests.py``; the runner isolates its own runs the same way
(``UnityTestRunner.isolate``): no real browser launch, one throwaway temp root
that every Unity child inherits, and the run's own owned project. At import
time, before a test module allocates anything. Best-effort, as in the runner.
"""

try:
    from run_tests import UnityTestRunner

    UnityTestRunner.isolate()
except Exception as _sandbox_error:  # noqa: BLE001
    print(f"[WARN] test sandbox unavailable ({_sandbox_error!r}); runs use real TEMP.")
