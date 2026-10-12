# P00 — Record decisions and establish baseline

**Status:** Completed historical milestone (2026-09-23).

This is historical context, not an active implementation specification. See the
[current use-case catalog](../simulation-use-case-plan.md).

## Retained outcome

The agreed normal graph design targets were 15 unique jobs by default, at most 50 per root, and depth at most eight including the root. These were future scenario limits, not properties enforced by the then-current three-job ETL factory.

## Historical context

The original host submitted two three-job roots to each queue once after one second: six roots and eighteen jobs. Source-mode and older-package baselines differed. Those old package failures are historical and do not establish a failure in the current preview package line.

## Current relationship

Use the [current graph-sizing guidance](../simulation-use-case-plan.md#graph-sizing). Current workloads remain explicit one-job/three-job graphs; graph limit enforcement belongs to the scenario that introduces it.
