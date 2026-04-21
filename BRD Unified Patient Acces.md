Business Requirements Document (BRD): Unified Patient Access & Clinical
Intelligence Platform 1. Executive Summary This project develops a
unified, standalone healthcare platform that bridges the gap between
patient scheduling and clinical data management. By combining a modern,
patient-centric appointment booking system with a \"Trust-First\"
clinical intelligence engine, the platform simplifies scheduling,
reduces no-show rates, and eliminates the manual extraction of patient
data from unstructured reports. The system serves patients,
administrative staff, and admins, providing a seamless end-to-end data
lifecycle from initial booking to post-visit data consolidation. 2. The
Business Problem & Market Opportunity Healthcare organizations suffer
from a disconnected data pipeline that creates inefficiencies at
multiple stages: • High No-Show Rates: Providers experience up to a 15%
no-show rate due to complex booking processes and lack of smart
reminders, leading to revenue loss and underutilized schedules. • Manual
Data Extraction: Clinical staff spend 20+ minutes manually reading
multi-format PDF reports to gather required patient data (vitals,
history, meds), which is a primary bottleneck in clinical prep. • Market
Gap: Existing solutions are fragmented; booking tools lack clinical data
context, and current AI coding tools face a \"Black Box\" trust deficit
where users must manually verify unlinked data. 3. Proposed Solution The
platform is an intelligent, integration-ready aggregator designed to
improve both operational scheduling and clinical prep. • Front-End
Booking: Delivers an intuitive scheduling experience with a dynamic
preferred slot swap feature, rule-based no-show risk assessment, and
flexible digital intake (AI conversational or manual fallback). •
Back-End Intelligence: Ingests patient-uploaded historical documents and
post-visit clinical notes to generate a unified, verified \"360-Degree
Patient View\" equipped with extracted ICD-10 and CPT codes,
transforming a 20-minute search task into a 2-minute verification
action. 4. Core Features & Differentiators • Flexible Patient Intake:
Patients can freely choose between an AI-assisted conversational intake
or a traditional manual form at any time, with edits easily handled
without forcing human assistance. • Dynamic Preferred Slot Swap:
Patients can book an available slot while selecting a preferred
unavailable slot; if the preferred slot opens, the system automatically
swaps the appointment and releases the original slot. • Centralized
Staff Control: Only staff members can handle walk-in bookings
(optionally creating an account for post-booking), manage same-day
queues, and mark patients as \"Arrived\". Patients cannot self-check in
via apps or QR codes. • Data Consolidation & Conflict Resolution: The
platform aggregates multiple documents to surface a de-duplicated
patient view, explicitly highlighting critical data conflicts (e.g.,
conflicting medications). 5. 5. Technology Stack & Infrastructure

To ensure a scalable, maintainable, and enterprise-grade architecture,
the platform will adopt a modern, layered microservices-based technology
stack as outlined below :

Frontend (UI Layer) Framework: Angular 18 (Standalone Components
Architecture) Architecture Approach: Component-based, modular design
with reactive programming using RxJS Forms Handling: Reactive Forms for
scalable and testable form management State Management (Optional): NgRx
/ Signals (based on complexity) Backend (API Layer) Framework: .NET 10
(ASP.NET Core Web API) Architecture Style: Microservices Architecture
Each core business domain (e.g., Appointment, Patient, Clinical Data)
will be implemented as independent services Backend Layered Architecture
(3-Tier) -(Controller Layer (Presentation Layer),Service Layer (Business
Logic Layer),Repository Layer (Data Access Layer)) ORM & Data Access ORM
Tool: Entity Framework Core (EF Core) Database Layer Database:
PostgreSQL

• Hosting & Infrastructure: The application must be hosted on free,
open-source-friendly platforms such as Netlify, Vercel, GitHub
Codespaces, or equivalent environments. Paid cloud hosting services
(e.g., AWS, Azure) are strictly out of scope for this phase. • Auxiliary
Processing & Utility Tools: For any additional system operations,
background processing, or data handling workflows, the platform must
exclusively utilize strictly free and open-source technology stacks and
tools. 6. Project Scope (Phase 1) In-Scope: • User Roles: Patients,
Staff (front desk/call center), and Admin (user management). • Booking &
Reminders: Appointment booking with waitlist functionality, automated
multi-channel reminders (SMS/Email), and Google/Outlook calendar sync
via free APIs. After booking, appointment details are sent as a PDF via
email. • Insurance Pre-Check: Soft validation of insurance name and ID
against an internal predefined set of dummy records. • Clinical Data
Aggregation: Core 360-Degree Data Extraction utilizing uploaded clinical
documents to build the patient profile. • Medical Coding: Mapping of
ICD-10 and CPT codes based on aggregated patient data. Out-of-Scope: •
Provider logins or provider-facing actions. • Payment gateway
integration (provisioning for future reservation fees only). • Family
member profile features. • Patient self-check-in (mobile, web portal, or
QR code). • Direct, bi-directional EHR integration or full claims
submission. • Use of paid cloud infrastructure (e.g., Azure). 7.
Non-Functional Requirements (NFRs) • Security & Compliance: 100%
HIPAA-compliant data handling, transmission, and storage. Strict
role-based access control and immutable audit logging for all patient
and staff actions. • Infrastructure: Native deployment capabilities
(Windows Services/IIS) using PostgreSQL for structured data and Upstash
Redis for caching. • Reliability: Targeting 99.9% uptime with robust
session management (15-minute automatic timeout). 8. High-Level Success
Criteria • Operational Efficiency: Demonstrable reduction in the
baseline no-show rate and a decrease in staff administrative time per
appointment. • Platform Adoption: High volume of total patient
dashboards created, and appointments successfully booked. • Clinical
Accuracy: An AI-Human Agreement Rate of \>98% for suggested clinical
data and medical codes. • Risk Prevention: Quantifiable metric of
\"Critical Conflicts Identified\" to track prevented safety risks and
claim denials.
