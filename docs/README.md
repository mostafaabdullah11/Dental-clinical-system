# Dental Clinic Management System — Design Package (Baseline v1.1)

**Design Baseline Version:** 1.1
**Status:** OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION (approved 2026-10-20)

This package is the Requirements + Architecture/Design baseline of a real production Dental Clinic Management System, synchronized with the Owner's final review and final business changes. **It contains documents only.** The Owner approved Design Baseline v1.1 on 2026-10-20; the Requirements + Architecture/Design package is **frozen** and is the authoritative V1 design baseline. No application code, no FRS and no implementation exist yet; the next phase is the FRS / implementation-ready functional specification and traceability.

**Design Baseline: v1.1 — Status: OWNER APPROVED — FROZEN FOR V1 IMPLEMENTATION.** The Markdown documents are the authoritative design documents; diagrams are Mermaid source only (no rendered diagrams are required or provided).

## What is in the package

| File | Read it for |
|---|---|
| `README.md` | This index |
| `DESIGN_BASELINE.md` | The one-page statement of what the system is and what is confirmed |
| `DESIGN_FREEZE.md` | Freeze statement, what changed in the final synchronization, conflict report, checklist |
| `OWNER_REVIEW_NOTES.md` | The Owner Decision Register: decisions, latest final changes, superseded rules, remaining items |
| `OPEN_QUESTIONS.md` | Answered Owner decisions; the two remaining items; technical interpretations |
| `DATABASE_DESIGN.md` | Schemas, tables, constraints, guards, cardinalities, tests |
| `SECURITY_DESIGN.md` | Roles, permission keys, privacy boundary, DB roles, restore security, tests |
| `FINANCE_DESIGN.md` | Charges, payments, Patient Credit, lab, payroll, reporting, finance proof, tests |
| `API_ARCHITECTURE.md` | API conventions, endpoint catalog, notifications and jobs, tests |
| `DEPLOYMENT_ARCHITECTURE.md` | Deployment, CI/CD, backup and restore, monitoring |
| `ARCHITECTURE_DECISIONS.md` | ADR-001 to ADR-018 |
| `IMPLEMENTATION_GUARDRAILS.md` | **Read before implementing any feature**; includes the 94-test traceability matrix, the 20 final-correction tests and the DO / DO NOT list |
| `DIAGRAMS.md` | All diagrams as Mermaid source with explanations |
| `diagrams/` | One `.mmd` Mermaid source file per diagram block (26), plus an index (`diagrams/README.md`) |

**Package size:** 14 Markdown files (13 above + `diagrams/README.md`) and 26 Mermaid sources = 40 files; no rendered image or PDF.

## Suggested reading order

1. `DESIGN_BASELINE.md`, then `OWNER_REVIEW_NOTES.md` (why the rules are what they are).
2. `DATABASE_DESIGN.md`, `SECURITY_DESIGN.md`, `FINANCE_DESIGN.md`, `API_ARCHITECTURE.md`.
3. `DEPLOYMENT_ARCHITECTURE.md`, `ARCHITECTURE_DECISIONS.md`.
4. `IMPLEMENTATION_GUARDRAILS.md` before any feature work.

## Diagrams: Mermaid source only

Diagrams are provided **only as Mermaid source** (`DIAGRAMS.md` and `diagrams/*.mmd`). **No PNG, SVG, JPG or PDF rendering is included, and none is promised.** Every `.mmd` file is parse-validated by the Mermaid parser; open the source in any Mermaid-capable viewer to see the picture.

## What is still deferred

All V1 business questions are answered. Only two deferred items remain, neither blocking implementation:

- **Q-ORT-3b — DEFERRED / OPEN FOR FUTURE VERSION:** what an Orthodontic case's *total price decrease* should do (V1 rejects it with `ortho.total_decrease_not_supported`; not a V1 blocker).
- **OPS-1 — DEFERRED:** the physical storage locations of the two backup recovery private keys (the Clinic Manager, as a person, holds both).

## Next step

Design Baseline v1.1 is Owner approved and frozen. The next phase is the FRS / implementation-ready functional specification and traceability (not part of this package). Any later business-rule change needs a controlled change request and a new approved baseline revision (DESIGN_FREEZE.md §7).
