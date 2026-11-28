# Productionisation roadmap for a UK bank

This is a gap-analysis checklist for evolving the demonstration into a
production mortgage service. It is not a claim of regulatory compliance, a
security certification, or legal advice. The bank's product owners, security,
privacy, compliance, architecture, resilience and operations teams must decide
which controls apply, set their required outcomes, and approve the design.
Requirements vary with the firm's permissions, business model, customer
journey, data and operating arrangements.

## What this prototype is (and is not)

The application uses fictional products and indicative eligibility rules. It
does not contact a credit bureau, make a real lending decision, send email, or
connect to a lender's systems. Do not enter real customer information or use it
to originate, assess or service a real mortgage.

The Azure deployment is a development/interview prototype, not a production
security boundary. In particular:

- Customer and staff web apps have public ingress. Staff sign-in uses Entra ID,
  but the app is not restricted to a bank network or managed device.
- SQL and Blob Storage use private endpoints, but the Service Bus Basic
  namespace and container registry are public endpoints. Basic Service Bus
  does not support Private Link.
- Customer abuse limiting is in-process and per replica. It is not a
  distributed control or a reliable client-identity boundary behind a proxy.
- Staff token caching is in memory. The separate notification worker and its
  scale-to-zero behavior still need live Azure validation.
- Audit entries are stored with application data; that alone does not provide
  a tamper-evident, centrally monitored security audit service.
- The deployment is single-region and does not provide a complete, tested
  recovery solution for every database, document, key and dependent service.
- There is no production support model, 24/7 monitoring, incident response
  process, independently approved release process or demonstrated service
  capacity.

These are examples of work to resolve, not an exhaustive risk assessment.

## Roadmap

### 1. Agree the business, customer and regulatory outcomes

- Identify the important business services this system would support, their
  customers and dependencies. Define availability and recovery objectives,
  customer impact tolerances, peak demand and acceptable degraded modes.
- Map the end-to-end customer and staff journeys, including application
  submission, document handling, customer support, assessment, decision,
  withdrawal, correction, complaints and failure recovery.
- Have the bank's compliance and legal teams determine which FCA/PRA rules,
  conduct requirements, records obligations and notifications apply. For a UK
  mortgage journey, assess the relevance of the FCA's
  [Consumer Duty](https://handbook.fca.org.uk/handbook/prin2a/) and
  [MCOB responsible-lending requirements](https://handbook.fca.org.uk/handbook/mcob11/).
  Applicability depends on the firm's role and the product; a technical
  implementation cannot establish compliance.
- Assess operational-resilience obligations and scenarios with the bank's
  resilience owners. Where applicable, use FCA
  [SYSC 15A](https://handbook.fca.org.uk/handbook/sysc15a/) to guide important
  business service mapping, impact tolerances and severe-but-plausible scenario
  testing.
- Classify personal, financial, identity and document data. Define the lawful
  purpose, minimisation, access, retention, deletion, subject-rights and
  disclosure requirements. Complete a DPIA when required; consult the bank's
  DPO and the ICO's [DPIA guidance](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/accountability-and-governance/guide-to-accountability-and-governance/data-protection-impact-assessments/).
- Assess Azure and every other material supplier as part of third-party risk
  management: data location, subcontractors, resilience, incident notice,
  audit/evidence, concentration risk, contractual controls and an achievable
  exit or continuity plan. PRA-regulated firms should have their compliance
  and procurement teams assess the current
  [PRA SS2/21 expectations](https://www.bankofengland.co.uk/prudential-regulation/publication/2021/march/outsourcing-and-third-party-risk-management-ss).

**Deliverables:** approved requirements and data classification, threat model,
privacy assessment, service/dependency map, supplier assessment, and an
architecture decision record with accountable owners.

### 2. Establish the bank's cloud and identity foundations

- Deploy into the bank's Azure landing zone and governance model, not an
  individual prototype subscription. Separate production, non-production and
  security/management responsibilities; use policy, resource locks where
  appropriate, approved regions, tagging, cost controls and configuration
  monitoring.
- Use a bank-approved network design with segmented subnets, controlled
  ingress and egress, private DNS, firewall policy and monitored routes.
  Expose customer traffic through an approved edge with WAF, bot/abuse
  protection and DDoS controls; prevent direct access to origins that bypasses
  those controls.
- Restrict staff access according to the bank's operating model: workforce
  identity, phishing-resistant MFA where required, Conditional Access,
  managed-device and risk policies, privileged access controls, and an
  approved private access path if the service is staff-only. Public reachability
  must not be mistaken for public authorization.
- Select an approved customer identity and account-recovery model if customers
  need persistent accounts or sensitive self-service. Design account recovery,
  step-up verification and support-agent access against account-takeover and
  social-engineering threats.
- Keep workload identities separate by app, environment and task. Remove
  unnecessary broad roles; use resource/queue-scoped least privilege, just-in-
  time privileged access, access reviews and separation of duties. Monitor
  emergency access and privileged role changes.
- Keep secrets out of source, images, pipelines and logs. Prefer managed
  identities and federated workload identity; store unavoidable secrets and
  encryption keys in the bank-approved vault/HSM service with rotation,
  ownership and recovery procedures.

**Deliverables:** approved landing-zone deployment, network and identity
design, access matrix, privileged-access process and tested environment
separation.

### 3. Harden application and data handling

- Put public web origins behind the approved edge; add centrally managed
  security headers, TLS policy, request-size limits, bot controls and
  distributed rate limiting. Review session lifetime, cookie settings,
  anti-forgery controls, enumeration, abuse cases and customer-support
  workflows.
- Use private connectivity for all supported data and messaging services.
  For Service Bus Private Link, evaluate Premium capacity and cost, configure
  private DNS and disable public network access after validating private
  connectivity. Restrict registry access and deployments as well. A private
  endpoint is one layer, not a replacement for identity authorization.
- Apply bank-approved encryption at rest and in transit, key ownership,
  rotation, separation and recovery. Determine whether customer-managed keys
  or HSM-backed keys are required. Encrypt and protect backups as well as
  primary data.
- Create a data lifecycle for applications, audit history, documents,
  notifications, caches and temporary files. Set retention and deletion from
  legal and business requirements rather than choosing an arbitrary duration.
  Define legal holds, correction, export and secure disposal.
- Add durable document safety controls: extension and content validation,
  malware scanning/quarantine, size limits, access logging, recovery and
  reconciliation of uploads whose database outcome is uncertain.
- Keep the current role and ownership checks as a foundation, then review
  authorization for every endpoint and object. Add controlled support access,
  strong staff session handling, break-glass procedures and auditable
  maker-checker approval for sensitive operational actions.
- If real affordability or eligibility assessment is introduced, replace the
  demonstration rules only under the bank's lending, model-risk, conduct and
  data-governance processes. Establish validated inputs, policy/rule versions,
  decision evidence, explainability, human review and correction/escalation
  paths; do not treat a sample calculation as a lending decision.

**Deliverables:** reviewed abuse cases and authorization matrix, data-flow
controls, verified private service paths, retention schedule and approved
decisioning/integration design.

### 4. Engineer resilience and recoverability

- Define RTO/RPO for each important business service and data store. Design
  zone resilience and regional recovery to meet those objectives; select the
  region pair and data-residency approach with the bank.
- Configure and verify backups for SQL, documents, keys and configuration.
  Set appropriate SQL point-in-time and long-term retention, Blob versioning
  or soft-delete/immutability protections, and isolated backup access. Ensure
  ransomware or production-admin compromise cannot trivially delete every
  recovery copy.
- Run restore and failover exercises regularly. Verify data correctness,
  access, keys, DNS, queues and application behavior after recovery, and record
  measured recovery times. A backup setting or restore command is not proof of
  recoverability.
- Make the submission/outbox/queue/worker path operationally complete:
  idempotent handlers, bounded retries, poison-message handling, alerting,
  controlled dead-letter inspection and replay, retention, duplicate
  protection, and tested recovery from partial failures. Prove behavior under
  queue outages, database outages and worker restarts.
- Load- and soak-test the full customer and staff journeys, including peak
  application periods, document sizes, database contention and downstream
  timeouts. Set capacity, scaling limits, quotas and graceful-degradation
  behavior from measured results.

**Deliverables:** approved continuity and recovery plans, tested backups and
failover, operational queue runbooks, and performance evidence against agreed
targets.

### 5. Operate it as a financial service

- Centralize security, identity, network, application, database and platform
  events in bank-approved monitoring/SIEM. Make privileged and sensitive
  business actions attributable and tamper-evident; restrict log access and
  retention, and avoid logging credentials, tokens or unnecessary customer
  data.
- Create actionable alerts for service health, anomalous access, authorization
  failures, data exfiltration indicators, queue age/dead letters, backup
  failures, capacity and security-control drift. Test alert delivery and
  escalation; do not depend on a single application log pipeline.
- Integrate vulnerability management, cloud security posture monitoring,
  endpoint/runtime protection and incident response with the bank's security
  operations centre. Define incident classification, evidence preservation,
  customer/regulator communications and supplier escalation with the relevant
  control owners.
- Provide named service ownership, on-call coverage, runbooks, support
  procedures, change windows, maintenance plans, service objectives and
  customer communications for planned and unplanned disruption.
- Review cloud-provider and other supplier continuity, incident and exit plans
  with the bank. Include dependency failures and loss of a region/provider in
  resilience exercises.

**Deliverables:** monitored service with tested runbooks, incident and
continuity procedures, service ownership and control evidence.

### 6. Make delivery and assurance suitable for production

- Separate deployment identities and permissions by environment. Require
  protected branches, peer review, change approval, production environment
  approval and separation between building and deploying. Keep federated
  credentials narrowly scoped and regularly reviewed.
- Add automated secret, dependency, license, source, container, IaC and
  configuration scanning. Produce and retain an SBOM and build provenance;
  pin and verify dependencies and base images, scan artifacts, and deploy
  immutable, approved image digests from a controlled registry.
- Test security and behavior in CI and pre-production: unit and integration
  tests, authorization tests, migration compatibility, DAST, penetration
  testing, load tests and recovery/failure scenarios. Track findings through
  risk-based remediation and formal exceptions.
- Use staged rollout, health gates and a tested rollback strategy. Apply
  expand-and-contract database changes, preserve backward compatibility
  during rollout, and rehearse rollback without losing submitted applications
  or audit evidence.
- Continuously assess configuration drift and policy compliance. Record
  release evidence, approvals, test results, migration outcomes and any
  accepted residual risk.

**Deliverables:** approved secure SDLC, controlled release pipeline, verified
artifact chain, security test evidence and production change records.

## Production go-live gate

Do not treat a successful build or deployment as approval to handle real
customer data. Before go-live, require explicit sign-off from the accountable
business owner and the bank's architecture, security, privacy, compliance,
resilience, data and operations owners. Confirm that:

- the applicable obligations, customer outcomes, risks and residual-risk
  owners are documented;
- the threat model and security assessment have no unresolved blocker;
- staff and customer access paths, least privilege and incident monitoring
  have been tested;
- data lifecycle, backup, restore, recovery and service exit have been
  exercised against approved objectives;
- load, failure, security and end-to-end business tests have passed; and
- support, on-call, customer communications, escalation and change procedures
  are staffed and rehearsed.

## Keep the useful prototype foundations

Productionisation does not automatically mean replacing the application with
microservices or Kubernetes. The existing separation between web apps, APIs,
data ownership and the notification worker, along with managed identities,
private SQL/Blob access, transactional audit writes, optimistic concurrency and
the submission outbox, are useful design foundations. Retain or evolve them
where they satisfy the bank's measured requirements and approved platform
standards; introduce complexity only to address a demonstrated requirement.
