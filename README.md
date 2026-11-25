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

Before starting Aspire, configure staff sign-in as described below. Aspire then starts a SQL Server container, the customer mortgage web app, employee portal, and Products API, then opens the Aspire dashboard. Both web apps use the same Azure SQL-compatible database for mortgage applications and audit history. Uploaded document contents are stored as shared local files under the current user's application-data directory, not in SQL. The migration removes the previous SQL document-content table only if it is empty; export any existing contents before applying it. Aspire applies EF Core migrations at startup, and a Docker data volume preserves the database across restarts. Wizard session state remains in each web process and is lost if that process restarts. Customer account access follows the application's existing owner identity; employee-created applications remain unassigned until staff explicitly links the customer's stable authentication subject ID. In VS Code, press **F5** and choose **PD Mortgages (Aspire)**, or use **Terminal → Run Task → run Aspire AppHost**. Docker Desktop must be running.

### Configure staff sign-in

The staff portal uses single-tenant Microsoft Entra ID OpenID Connect. Create a development app registration with Web redirect URI `https://localhost:7241/signin-oidc` and front-channel sign-out callback `https://localhost:7241/signout-callback-oidc`. Add an app role whose value is `MortgageStaff`, require assignment on the enterprise application, and assign your staff account or group to the role. Then store the registration values locally with User Secrets:

```bash
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "AzureAd:ClientId" "<development-app-client-id>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "AzureAd:ClientSecret" "<development-app-secret>" --project PD.Mortgages.Staff.Web
```

Do not commit the client secret. The staff app fails at startup if these Entra settings are missing. Use a separate production app registration and provide `AzureAd__TenantId`, `AzureAd__ClientId`, and `AzureAd__ClientSecret` through secure Azure configuration (preferably Key Vault); register the production callback URL and assign the `MortgageStaff` role there too. The pipeline currently builds and pushes the staff image but does not deploy a staff Container App, so Azure hosting resources still need to be configured before production use.

After configuration, the employee portal opens at its `mortgages-staff-web` URL. All staff MVC routes require an authenticated Entra user with the `MortgageStaff` app role. The app records the stable tenant/object ID pair as the staff actor, not an email address. It provides a recent-application dashboard with applicant/email/reference/creator search, status filtering, and 25-result pages. Creating or editing a draft follows the same eight-step sequence as the customer journey: property, applicant, finances, consents, Decision in Principle, product, optional documents, and review. Staff can link an unassigned application to a customer's exact authentication subject ID; the link is audited and cannot be changed after assignment. Customer sign-in and identity lookup are not configured in this local build, so linking stores an ID but cannot verify or create a customer account.

### Run only the mortgage web app

```bash
dotnet restore PD.Mortgages.slnx
dotnet build PD.Mortgages.slnx
dotnet run --project PD.Mortgages.Web
```

This standalone mode uses the development mock product catalog and in-memory application, audit, document, and session stores; it does not start SQL Server. Open the local URL printed by `dotnet run`. All application data is lost when the web app restarts.

### Run and debug in VS Code

Open the solution folder in VS Code and install the recommended C# Dev Kit extension. Aspire runs the customer web app, staff web app, and Products API as .NET projects and SQL Server in a Docker container.

## Azure DevOps and Azure Container Apps

`azure-pipelines.yml` builds and tests the solution on pull requests and pushes to `main`. On `main`, it provisions the Azure foundation with `infra/main.bicep`, builds and pushes version-tagged images for all three web projects to Azure Container Registry, then deploys the customer web app and internal Products API to Azure Container Apps. Bicep provisions Azure Files for the file-backed demo data, a Log Analytics workspace, and managed identity-based registry pulls. The customer app uses a single replica and in-memory sessions; sessions are lost when that replica restarts.

Before running the pipeline:

1. Create an Azure Resource Group and an Azure DevOps Azure Resource Manager service connection scoped to it. The service connection identity needs Contributor and permission to create the narrowly scoped ACR role assignments in the Bicep template (for example, User Access Administrator at the resource-group scope).
2. Edit the pipeline variables at the top of `azure-pipelines.yml`: service connection name, resource group, location, globally unique lowercase ACR and storage-account names, the service connection's service-principal object ID, and app/environment names.
3. Run the pipeline from `main`. The first Bicep deployment provisions the registry and shared infrastructure, then the pipeline pushes images and deploys the app revisions. No credentials or API keys are stored in the repository; the pipeline generates the internal Products API key and passes it as a secure Bicep parameter.

**Important:** this repository is a synthetic demonstration, not production mortgage software. Staff sign-in is implemented, but the staff Container App is not deployed by the pipeline and requires production Entra app registration, secure secret configuration, and an Azure Container App resource before use. The customer app also lacks configured production authentication. Persist ASP.NET Core Data Protection keys across staff app restarts/replicas before production deployment. Do not put real customer, financial, or identity data into this demonstration; review authentication, authorization, ingress, data protection, and operational controls before production use.

## Demonstration limitations

PD Mortgages, its rates, products, estimates, and statuses are fictional. Credit checks, open banking, underwriting, and lender submission are not connected to external services. Local Aspire stores uploaded documents in shared local files, not SQL Server; the existing Azure deployment still uses file-backed persistence and Azure Files. Configure Azure SQL for application and audit data, Azure Blob Storage for documents, real authentication, authorization, and audit retention before production use. Do not use this demonstration with real financial or identity data.
