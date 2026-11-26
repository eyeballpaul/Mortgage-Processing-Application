# PD Mortgages

A .NET 10 ASP.NET Core MVC demonstration of a UK mortgage application journey. It uses Razor views, Bootstrap 5, server-side DataAnnotations validation, and session state. Standalone development can use in-memory storage; Aspire runs the apps against separate mortgage and product databases on one local SQL Server container, with document contents stored as files. No live lender, credit-check, or open-banking integrations are connected.

## Architecture

See [docs/architecture.md](docs/architecture.md) for local runtime diagrams, persistence modes, and the current Azure deployment.

```text
PD.Mortgages.AppHost
├── PD.Mortgages.Web ────────────┐
├── PD.Mortgages.Staff.Web ──────┴──> PD.Mortgages.Application -> PD.Mortgages.Domain
├── PD.Products.Api
└── SQL Server (local Aspire; mortgages and products databases)

Both mortgage web apps -> PD.Mortgages.Infrastructure -> PD.Mortgages.Domain
```

- **Domain** contains the mortgage application aggregate, product and document entities, application enums, the `LoanToValue` value object, eligibility domain service, and repository/storage contracts. It has no project dependencies.
- **Application** contains DTOs and the application service that coordinates persistence, product eligibility, document storage, and submission.
- **Infrastructure** implements in-memory, file-backed, and EF Core SQL Server persistence for applications and audit history, in-memory, file, and Azure Blob document storage, and product-service clients.
- **Web** contains the customer MVC controllers, annotated view models, strongly typed Razor pages, session configuration, layout, and responsive design system.
- **Staff.Web** is the employee-facing MVC portal. It reuses the same application service, domain rules, repository, product catalog, styling, and application files.
- **Products.Api** exposes the product catalog over HTTP. Aspire and Azure use a SQL-backed catalog; standalone runs use the in-memory catalog.

The application flow is: property and deposit → applicant details → income and commitments → optional credit/open-banking consent → an indicative Decision in Principle guide → eligible example products → optional document upload → review and submission. The DIP page displays a loading state while the backend runs a simulated assessment with a two-second delay. The resulting guide is a simple 4.5× annual-income illustration only; it is not a lender decision, does not contact a credit agency, and is not an offer or guarantee. Customers can continue an in-progress application only in the same browser session; submitted applications are available to staff, not through the customer journey.

## Run

### Run the solution with Aspire

```bash
dotnet run --project PD.Mortgages.AppHost --launch-profile https
```

Before starting Aspire, configure staff sign-in as described below. Aspire starts a local SQL Server container, the customer mortgage web app, employee portal, and Products API, then opens the Aspire dashboard. The web apps share the `mortgages` database for application and audit data; the API uses a separate `products` database in the same SQL Server container. Its migration seeds the current example products. Uploaded document contents are stored as shared local files under the current user's application-data directory, not in SQL. The mortgage and product `InitialCreate` migrations are baselines for fresh databases; they do not upgrade older schemas. Back up older databases and prepare deliberate migrations or resets before using these baselines. A Docker data volume preserves both databases across restarts. Customer wizard session state is in memory and is lost if the customer web process restarts. In VS Code, press **F5** and choose **PD Mortgages (Aspire)**, or use **Terminal → Run Task → run Aspire AppHost**. Docker Desktop must be running.

### Configure staff sign-in

The staff portal uses single-tenant Microsoft Entra ID OpenID Connect with the authorization-code flow. Create a development app registration with Web redirect URI `https://localhost:7241/signin-oidc` and front-channel sign-out callback `https://localhost:7241/signout-callback-oidc`; the app does not require the implicit ID-token grant. In **App registrations → App roles**, create an enabled role that allows **Users/Groups** and has the exact **Value** `MortgageStaff`. In the matching **Enterprise application** (same Application/Client ID), assign your staff user or group to that role; enable **Assignment required** if you want Entra to block unassigned users before they reach the app. Then store the registration values locally with User Secrets:

```bash
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "AzureAd:ClientId" "<development-app-client-id>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "AzureAd:ClientSecret" "<development-app-secret>" --project PD.Mortgages.Staff.Web
```

Use the client secret **Value**, not its ID; Azure only displays the value when the secret is created. Do not commit it. The staff app fails at startup if these Entra settings are missing. Use a separate production app registration and provide `AzureAd__TenantId`, `AzureAd__ClientId`, and `AzureAd__ClientSecret` through secure Azure configuration (preferably Key Vault); register the production callback URL and assign the `MortgageStaff` role there too. The pipeline currently builds and pushes the staff image but does not deploy a staff Container App, so Azure hosting resources still need to be configured before production use.

After configuration, the employee portal opens at its `mortgages-staff-web` URL. All staff MVC routes require an authenticated Entra user with the `MortgageStaff` app role. The app records the stable tenant/object ID pair as the staff actor, not an email address. It provides a recent-application dashboard with applicant/email/reference/creator search, status filtering, and 25-result pages. Creating or editing a draft follows the same eight-step sequence as the customer journey: property, applicant, finances, consents, Decision in Principle, product, optional documents, and review.

### Run only the mortgage web app

```bash
dotnet restore PD.Mortgages.slnx
dotnet build PD.Mortgages.slnx
dotnet run --project PD.Mortgages.Web
```

This starts only the customer app. It uses the development mock product catalog and in-memory application, audit, document, and session stores; it does not start SQL Server. Open the local URL printed by `dotnet run`. All application data is lost when the web app restarts. Running the staff project directly instead requires the Entra settings above and defaults to file-backed persistence under `data`; it will not share records with this standalone customer app. Use Aspire when the mortgage apps need shared SQL persistence and the Products API needs its SQL-backed catalog.

### Run and debug in VS Code

Open the solution folder in VS Code and install the recommended C# Dev Kit extension. Aspire runs the customer web app, staff web app, and Products API as .NET projects and SQL Server in a Docker container.

## Tests

Run the test suite with:

```bash
dotnet test PD.Mortgages.slnx
```

Tests run without starting Aspire or connecting to SQL Server. Most mortgage service tests use in-memory repositories; durable-storage tests use isolated temporary files. SQL persistence tests check service registration only and do not connect to a database. Products API tests run in-process with its in-memory catalog. SQL queries and migrations are not currently covered by database integration tests.

## Azure DevOps and Azure Container Apps

`azure-pipelines.yml` builds and tests the solution on pull requests and pushes to `main`. On `main`, it provisions Azure SQL, private document/Data Protection Blob containers, a Log Analytics workspace, and managed identities; then it bootstraps identity access and applies EF migrations before building/pushing images and deploying the customer app and internal Products API. The products API uses the durable SQL catalog; the customer app uses SQL for application/audit data and distributed session state. Runtime services use managed identity, SQL authentication is disabled, Blob shared-key access is disabled, and containers have no anonymous access.

Before running the pipeline:

1. Create an Azure Resource Group and an Azure DevOps Azure Resource Manager service connection scoped to it. The service connection identity needs Contributor, SQL server Entra administrator privileges through the Bicep configuration, and permission to create the narrowly scoped ACR/Blob role assignments (for example, User Access Administrator at the resource-group scope).
2. Edit the pipeline variables at the top of `azure-pipelines.yml`: service connection name, resource group, location, globally unique lowercase ACR, SQL server and Blob storage-account names, the service connection's service-principal object ID and display name, and app/environment names.
3. Run the pipeline from `main`. The pipeline generates a bootstrap SQL password (then enables Entra-only SQL authentication), bootstraps the mortgage/products managed identities, applies database migrations, pushes version-tagged images, and deploys the apps. No credentials or API keys are stored in the repository; the internal Products API key and bootstrap SQL password are generated at deployment and passed as secure Bicep parameters.

The Azure SQL server and storage account are currently reachable through public Azure endpoints; SQL access is restricted to Azure services and Entra identities, and Blob access requires managed identity. Add private endpoints and VNet integration before handling sensitive production data. The migration bootstrap creates only runtime read/write roles; the pipeline service connection performs schema migrations. Managed-identity database users are created using their client IDs. If a previous bootstrap created a user with an incorrect SID, bootstrap stops rather than changing it; an administrator must verify and recreate that user before retrying.

### Database migrations

The mortgage and product `InitialCreate` migrations are baselines for new databases. The subsequent mortgage session-cache migration is retained separately so databases that already applied the baseline can upgrade. The deployment bootstrap applies pending migrations to both databases before deploying app revisions; local Aspire applies them at startup. Do not edit or remove applied migrations: add new migrations for model changes.

**Important:** this repository is a synthetic demonstration, not production mortgage software. Staff sign-in is implemented, but the staff Container App is not deployed by the pipeline and requires production Entra app registration and an Azure Container App resource before use. The customer journey uses a browser session for an in-progress application and ends access upon submission. The session expires after 30 days of inactivity or when its browser cookie expires; anyone with access to that browser session can see its draft, so customers should avoid shared devices. Persisted Data Protection keys are configured for the customer app and supported by the staff app when `DataProtection:BlobUri` is supplied. Do not put real customer, financial, or identity data into this demonstration.

## Demonstration limitations

PD Mortgages, its rates, products, estimates, and statuses are fictional. Credit checks, open banking, underwriting, and lender submission are not connected to external services. Local Aspire stores uploaded documents in shared local files, not SQL Server. Azure uses SQL and Blob Storage, but has no private endpoints or production audit-retention policy. Do not use this demonstration with real financial or identity data.
