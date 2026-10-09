# diagrams/ index

**Design Baseline Version:** 1.1
**Status:** PROPOSED FREEZE — AWAITING OWNER APPROVAL

Each `.mmd` file is the Mermaid source of one block in `../DIAGRAMS.md` (which is the authoritative document). Rendered outputs are in `../rendered-diagrams/` (`.svg` and `.png`, same base name). All 26 blocks were re-compiled with Mermaid CLI after the v1.1 scheduling correction (Diagrams 5 and 8 changed) with no syntax errors.

| Diagram | Source | Rendered |
|---|---|---|
| Diagram 1: System Context | `diagram-01-system-context.mmd` | `diagram-01-system-context.svg`, `diagram-01-system-context.png` |
| Diagram 2: High Level Application Architecture | `diagram-02-high-level-application-architecture.mmd` | `diagram-02-high-level-application-architecture.svg`, `diagram-02-high-level-application-architecture.png` |
| Diagram 3: Module Dependency | `diagram-03-module-dependency.mmd` | `diagram-03-module-dependency.svg`, `diagram-03-module-dependency.png` |
| Diagram 4: Role Permission | `diagram-04-role-permission.mmd` | `diagram-04-role-permission.svg`, `diagram-04-role-permission.png` |
| Diagram 5: Patient Lifecycle | `diagram-05-patient-lifecycle.mmd` | `diagram-05-patient-lifecycle.svg`, `diagram-05-patient-lifecycle.png` |
| Diagram 6: Doctor Treatment Workflow | `diagram-06-doctor-treatment-workflow.mmd` | `diagram-06-doctor-treatment-workflow.svg`, `diagram-06-doctor-treatment-workflow.png` |
| Diagram 7: Root Canal Workflow | `diagram-07-root-canal-workflow.mmd` | `diagram-07-root-canal-workflow.svg`, `diagram-07-root-canal-workflow.png` |
| Diagram 8: Appointment Workflow | `diagram-08-appointment-workflow.mmd` | `diagram-08-appointment-workflow.svg`, `diagram-08-appointment-workflow.png` |
| Diagram 9: Doctor Weekly Availability | `diagram-09-doctor-weekly-availability.mmd` | `diagram-09-doctor-weekly-availability.svg`, `diagram-09-doctor-weekly-availability.png` |
| Diagram 10: Implant Workflow | `diagram-10-implant-workflow.mmd` | `diagram-10-implant-workflow.svg`, `diagram-10-implant-workflow.png` |
| Diagram 11: Orthodontic Workflow | `diagram-11-orthodontic-workflow.mmd` | `diagram-11-orthodontic-workflow.svg`, `diagram-11-orthodontic-workflow.png` |
| Diagram 12: Referral Workflow | `diagram-12-referral-workflow.mmd` | `diagram-12-referral-workflow.svg`, `diagram-12-referral-workflow.png` |
| Diagram 13: Prescription Workflow | `diagram-13-prescription-workflow.mmd` | `diagram-13-prescription-workflow.svg`, `diagram-13-prescription-workflow.png` |
| Diagram 14: Finance Workflow | `diagram-14-finance-workflow.mmd` | `diagram-14-finance-workflow.svg`, `diagram-14-finance-workflow.png` |
| Diagram 15: Lab Expense Workflow | `diagram-15-lab-expense-workflow.mmd` | `diagram-15-lab-expense-workflow.svg`, `diagram-15-lab-expense-workflow.png` |
| Diagram 16: Doctor Payroll Workflow | `diagram-16-doctor-payroll-workflow.mmd` | `diagram-16-doctor-payroll-workflow.svg`, `diagram-16-doctor-payroll-workflow.png` |
| Diagram 17: Sales Payroll Workflow | `diagram-17-sales-payroll-workflow.mmd` | `diagram-17-sales-payroll-workflow.svg`, `diagram-17-sales-payroll-workflow.png` |
| Diagram 18: Clinic Operating Expense Workflow | `diagram-18-clinic-operating-expense-workflow.mmd` | `diagram-18-clinic-operating-expense-workflow.svg`, `diagram-18-clinic-operating-expense-workflow.png` |
| Diagram 19: Notification Background Job Workflow | `diagram-19-notification-background-job-workflow.mmd` | `diagram-19-notification-background-job-workflow.svg`, `diagram-19-notification-background-job-workflow.png` |
| Diagram 20: Feedback Workflow | `diagram-20-feedback-workflow.mmd` | `diagram-20-feedback-workflow.svg`, `diagram-20-feedback-workflow.png` |
| Diagram 21: Deployment Architecture | `diagram-21-deployment-architecture.mmd` | `diagram-21-deployment-architecture.svg`, `diagram-21-deployment-architecture.png` |
| ERD 22a: Identity Security | `erd-22a-identity-security.mmd` | `erd-22a-identity-security.svg`, `erd-22a-identity-security.png` |
| ERD 22b: Scheduling Crm | `erd-22b-scheduling-crm.mmd` | `erd-22b-scheduling-crm.svg`, `erd-22b-scheduling-crm.png` |
| ERD 22c: Clinical | `erd-22c-clinical.mmd` | `erd-22c-clinical.svg`, `erd-22c-clinical.png` |
| ERD 22d: Financial Including Orthodontics And Payroll | `erd-22d-financial-including-orthodontics-and-payroll.mmd` | `erd-22d-financial-including-orthodontics-and-payroll.svg`, `erd-22d-financial-including-orthodontics-and-payroll.png` |
| Diagram 23: Assistant Payroll Workflow | `diagram-23-assistant-payroll-workflow.mmd` | `diagram-23-assistant-payroll-workflow.svg`, `diagram-23-assistant-payroll-workflow.png` |

v1.1: **Diagram 23 — Assistant Payroll Workflow** is new; ERD 22d gained `ASSISTANTS` and `ASSISTANT_PAYROLLS`. There is no Assistant clinical, patient or login diagram by design. Inventory and X-ray have no diagram.

v1.1 scheduling correction: Diagram 5 has no Check-In node; Diagram 8 shows exactly the 7 appointment states.
