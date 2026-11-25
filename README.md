# PD Mortgages

A .NET 10 ASP.NET Core MVC demonstration of a UK mortgage application journey. It uses Razor views, Bootstrap 5, server-side DataAnnotations validation, session state, and configurable in-memory or file-backed repositories. No database or external financial integrations are required.

## Architecture

```text
PD.Mortgages.Web ─────┐
                      ├-> PD.Mortgages.Application -> PD.Mortgages.Domain
PD.Mortgages.Staff.Web┘          |
                                 └-> PD.Mortgages.Infrastructure -> PD.Mortgages.Domain
```

- **Domain** contains the mortgage application aggregate, product and document entities, application enums, the `LoanToValue` value object, eligibility domain service, and repository/storage contracts. It has no project dependencies.
- **Application** contains DTOs and the application service that coordinates persistence, product eligibility, document storage, and submission.
- **Infrastructure** implements the domain contracts with thread-safe in-memory and file-backed repositories, document storage, and realistic example mortgage products.
- **Web** contains the customer MVC controllers, annotated view models, strongly typed Razor pages, session configuration, layout, and responsive design system.
- **Staff.Web** is the employee-facing MVC portal. It reuses the same application service, domain rules, repository, product catalog, styling, and application files.

The application flow is: property and deposit → applicant details → income and commitments → optional credit/open-banking consent → an indicative Decision in Principle guide → eligible example products → optional document upload → review and submission. The DIP page displays a loading state while the backend runs a simulated assessment with a two-second delay. The resulting guide is a simple 4.5× annual-income illustration only; it is not a lender decision, does not contact a credit agency, and is not an offer or guarantee. A dashboard summarizes status, timeline, DIP guide, product, and documents.

## Run

### Run the solution with Aspire

```bash
dotnet run --project PD.Mortgages.AppHost --launch-profile https
```

Aspire starts the customer mortgage web app, employee portal, and Products API together and opens the Aspire dashboard. Both web apps use the same application services, but each process has its own in-memory applications, audit history, documents, and session state. Records are not shared between the portals and are lost when their respective process restarts. Customer account access follows the application's existing owner identity; employee-created applications remain unassigned until staff explicitly links the customer's stable authentication subject ID. In VS Code, press **F5** and choose **PD Mortgages (Aspire)**, or use **Terminal → Run Task → run Aspire AppHost**.

The employee portal opens at its `mortgages-staff-web` URL. It provides a recent-application dashboard with applicant/email/reference/creator search, status filtering, and 25-result pages. Creating or editing a draft follows the same eight-step sequence as the customer journey: property, applicant, finances, consents, Decision in Principle, product, optional documents, and review. Employees use the existing domain workflow rules, including Decision in Principle generation and eligible-product validation. The staff Decision in Principle step shows a loading spinner during the simulated assessment and enables a direct, guarded link to mortgage options when it completes. Staff-created applications store the creating employee's stable identity ID separately from the customer owner, show it in the staff dashboard and application details, and allow searching by creator. Customer-created applications have no employee creator. Staff can link an unassigned application to a customer's exact authentication subject ID; the link is audited and cannot be changed after assignment. Customer sign-in and identity lookup are not configured in this local build, so linking stores an ID but cannot verify or create a customer account. The portal uses a development staff identity and deliberately refuses to run outside the Development environment. Configure real employee authentication/authorization and a trusted customer identity lookup before any non-development use.

### Run only the mortgage web app

```bash
dotnet restore PD.Mortgages.slnx
dotnet build PD.Mortgages.slnx
dotnet run --project PD.Mortgages.Web
```

This standalone mode uses the development mock product catalog and in-memory application, audit, document, and session stores. Open the local URL printed by `dotnet run`. All application data is lost when the web app restarts.

### Run and debug in VS Code

Open the solution folder in VS Code and install the recommended C# Dev Kit extension. Aspire runs the customer web app, staff web app, and Products API as separate .NET projects; it does not require Docker for these projects.

## Azure DevOps and Azure Container Apps

`azure-pipelines.yml` builds and tests the solution on pull requests and pushes to `main`. On `main`, it provisions the Azure foundation with `infra/main.bicep`, builds and pushes version-tagged images for all three web projects to Azure Container Registry, then deploys the customer web app and internal Products API to Azure Container Apps. Bicep provisions Azure Files for the file-backed demo data, a Log Analytics workspace, and managed identity-based registry pulls. The customer app uses a single replica and in-memory sessions; sessions are lost when that replica restarts.

Before running the pipeline:

1. Create an Azure Resource Group and an Azure DevOps Azure Resource Manager service connection scoped to it. The service connection identity needs Contributor and permission to create the narrowly scoped ACR role assignments in the Bicep template (for example, User Access Administrator at the resource-group scope).
2. Edit the pipeline variables at the top of `azure-pipelines.yml`: service connection name, resource group, location, globally unique lowercase ACR and storage-account names, the service connection's service-principal object ID, and app/environment names.
3. Run the pipeline from `main`. The first Bicep deployment provisions the registry and shared infrastructure, then the pipeline pushes images and deploys the app revisions. No credentials or API keys are stored in the repository; the pipeline generates the internal Products API key and passes it as a secure Bicep parameter.

**Important:** this repository is a synthetic demonstration, not production mortgage software. The staff portal deliberately refuses to run outside Development and has no real staff authentication, so its image is built and pushed but the staff app is not deployed. The customer app also lacks configured production authentication. Do not put real customer, financial, or identity data into this deployment; configure and review real authentication/authorization and operational controls before exposing it for anything beyond a demo.

## Demonstration limitations

PD Mortgages, its rates, products, estimates, and statuses are fictional. Credit checks, open banking, underwriting, and lender submission are not connected to external services. Uploaded PDFs and images are stored on local disk with size/type checks. File persistence is a local demonstration option, not production-grade shared storage; the employee portal is development-only until real authentication, authorization, audit retention, and production persistence are configured. Do not use this demonstration with real financial or identity data.
