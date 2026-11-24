# ADR 0003: Transparent, calibratable effort model

- Status: Accepted
- Date: 2026-11-23

## Context

Effort estimates are only trusted if people can see how they were produced and adjust them to their team.

## Decision

- Each rule carries `basePoints` and `scaling`: `Log` (`basePoints × (1 + log2(count))`) for repetitive mechanical
  fixes, `Linear` (`basePoints × count`) for per-unit work such as Web Forms pages or WCF contracts.
- Project P50 multiplies the sum by a size factor (log10 of LOC) and a coupling factor (number of dependents, capped).
- P90 widens with the share of critical points: `P50 × (1.3 + 0.7 × criticalShare)`.
- Points convert to hours via `--hours-per-point`; rule points can be overridden with `--rules <file>`.
- Reports show the top cost drivers per project.

## Consequences

- Estimates are heuristics; calibration against a completed migration is the documented way to make them meaningful.
- `lms calibrate` fits `hoursPerPoint` from historical data as `Σ actual hours / Σ P50 points` (ratio of sums:
  unbiased in total, explainable) and reports per-project error and P90 coverage. Per-rule point fitting is
  deliberately left out: a single past migration has too few data points and would overfit.
