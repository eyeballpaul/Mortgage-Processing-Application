# PD Mortgages

A .NET 10 demonstration of a UK mortgage submission journey. The figures, products and decisions are fictional: no lender or credit-check integrations are connected. Do not use real customer data.

The customer and staff journeys record an optional credit-check choice. Declining it does not prevent an application from continuing.

## Architecture

The customer and staff MVC apps call the internal **Mortgage API**; neither connects to the mortgage database or document storage. The API owns the application service, mortgage and audit data and document storage, and calls the internal Products and stateless Eligibility APIs. The customer app owns a separate SQL database for its distributed session cache. The staff app has no database because it has no independent data to persist. Both apps use separate Blob-backed Data Protection key stores in Azure.

```text
Customer browser -> Customer web -> Mortgage API -> mortgage SQL + document Blob
                         |               |
                  customer sessions SQL  -> Products API -> products SQL
                                         -> Eligibility API (no database)
Staff browser -> Staff web --------^
                     | delegated staff identity
                     -> Microsoft Entra ID
```

Customer submissions are anonymous. A random server-side session actor owns each draft; the customer cannot reopen it after submission. A submission can be safely retried using the same session-bound attempt key and application version; a minimal reference and submission time can be recovered for 24 hours in the same session. Losing the session prevents recovery. Staff must sign in with Entra and have the `MortgageStaff` role. The API validates both the calling app and the delegated staff identity, scope and role; audit entries use the validated tenant/object IDs. In Azure the customer app uses a managed identity with the API's `MortgageCustomer` application role. Browser-supplied identity or role headers are not accepted as staff identity.

Starting or restarting a customer application requires an anti-forgery-protected POST and is limited to five attempts per observed remote address per ten minutes per web replica. Behind a reverse proxy, this may be the proxy's address, so multiple customers can share a limit. This in-process limit is a basic demo safeguard, not a distributed abuse-prevention boundary; use a trusted edge rate limiter for production.

In-progress customer and staff drafts expire after 30 days without authorized customer or staff activity; rejected reads do not extend retention, and submitted applications are not expired. Deletion or expiry records an audit event and leaves a minimal tombstone without the application details. While the Mortgage API is running, it retries document cleanup in the background and rechecks tombstones daily to remove any late-arriving uploads; cleanup waits if the API is stopped. Audit events and tombstones are retained; choose an appropriate retention policy before using real data.

An uncertain document upload or database commit retains the uploaded content instead of risking deletion of a committed document. The API logs the application and document IDs for investigation. Orphaned content for active applications is **not automatically reconciled**: inspect these warnings, confirm the final application metadata and wait for any in-flight operations before manually removing unreferenced blobs. Do not rely on this prototype for important documents without durable upload reconciliation and tested Blob recovery.

See [architecture.md](docs/architecture.md) for boundaries, local and Azure hosting, security and limitations.

## Local development with Aspire

Install .NET 10, Docker Desktop, and the trusted ASP.NET Core development certificate (`dotnet dev-certs https --trust`). Configure the staff portal and API Entra registrations below, then:

```bash
dotnet run --project PD.Mortgages.AppHost --launch-profile https
```

Aspire starts both web apps, the internal Mortgage, Products and Eligibility APIs, and one SQL Server container with separate `mortgages`, `products`, and `customer-sessions` databases. It generates separate, process-scoped keys for Mortgage's calls to Products and Eligibility; no local service key needs to be configured manually.

Aspire runs the .NET apps locally; its Docker container is for SQL Server. For a container image, build from the repository root using the relevant service Dockerfile, for example `docker build -f PD.Mortgages.Web/Dockerfile .`. Azure deployment uses the five service images and a separate database-bootstrap image.

The Mortgage API stores documents under `LocalApplicationData/PD.Mortgages`; the SQL Docker volume persists data across restarts. The APIs migrate the mortgage and product databases locally at startup, and the customer web app migrates the customer-session database. Aspire creates a fresh, process-scoped development credential for customer-to-API calls; it is accepted **only** by an API running in Development. Staff still need real Entra sign-in and a delegated API token.

### Configure Entra for staff and the API

Use separate development and production registrations in one tenant. For each environment:

1. Create a single-tenant **Mortgage API** app registration. Set its Application ID URI to `api://<mortgage-api-client-id>` and `requestedAccessTokenVersion` to `2` in its manifest. Under **Expose an API**, add the enabled delegated scope `Mortgage.Staff`. Add enabled app roles with values exactly `MortgageStaff` (Users/Groups) and `MortgageCustomer` (Applications). Ensure the corresponding enterprise application exists.
2. Create the single-tenant **staff web** registration. Add Web redirect URI `https://localhost:7241/signin-oidc` and sign-out callback `https://localhost:7241/signout-callback-oidc` for development; add the deployed staff URL's corresponding paths for Azure. Create a client secret and an enabled Users/Groups app role with value exactly `MortgageStaff`. The role value, not just its display name, must match.
3. Give the staff registration the Mortgage API's delegated `Mortgage.Staff` permission and grant administrator consent. Assign each staff user or group to `MortgageStaff` on **both** enterprise applications: the staff web role controls sign-in, and the API role appears in its access tokens. If assignment is required, the customer managed identity also needs the API's `MortgageCustomer` role.
4. Configure the staff app and Aspire (replace placeholders; do not commit secrets):

```bash
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "AzureAd:ClientId" "<staff-web-client-id>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "AzureAd:ClientSecret" "<staff-web-secret-value>" --project PD.Mortgages.Staff.Web
dotnet user-secrets set "MortgageApi:TenantId" "<tenant-id>" --project PD.Mortgages.AppHost
dotnet user-secrets set "MortgageApi:ApiClientId" "<mortgage-api-client-id>" --project PD.Mortgages.AppHost
dotnet user-secrets set "MortgageApi:StaffClientId" "<staff-web-client-id>" --project PD.Mortgages.AppHost
```

Use the secret **value**, not its ID. Aspire configures the API and staff scope from these values. The staff portal fails closed without Entra configuration; the Development customer credential cannot authorize staff calls. For separate standalone processes, configure matching `MortgageApi__DevelopmentKey` values of at least 32 characters on the API and customer app, with `ASPNETCORE_ENVIRONMENT=Development`, and configure `MortgageApi__BaseUrl` and `MortgageApi__Scope` on the web app. Never use a development key in Azure.

The API's [HTTP examples](PD.Mortgages.Api/PD.Mortgages.Api.http) illustrate all operations with bearer tokens. Mutations require the latest application version in the `X-Expected-Version` header; stale edits return a conflict rather than overwriting changes. Development OpenAPI is available at `/openapi/v1.json`.

## Tests

```bash
dotnet test PD.Mortgages.slnx
```

Tests include API authorization, cross-session isolation, submission, staff audit attribution, document validation, draft lifecycle, concurrent edits, and migration shape. Test tokens are signed locally, not issued by Entra; an actual staff login and Azure deployment require the tenant setup above.

## Azure prototype deployment

The [Azure deployment guide](AZURE_DEPLOYMENT.md) covers the interactive quickstart, Entra permissions, GitHub Actions setup, costs and recreation after resource-group deletion. The quickstart provisions one VNet with Container Apps and private-endpoint subnets, five Container Apps, three SQL databases, private SQL and Blob endpoints, and a private database-bootstrap job. Both web apps have public HTTPS ingress; the three APIs have internal ingress. SQL and Blob public network access is disabled. Staff access still requires Entra authentication and the staff app is not private-network-only. Use fictional data only; this is not a production security boundary.

The [GitHub Actions workflow](.github/workflows/ci.yml) tests pull requests and, after tests succeed on a push to `main`, deploys changed application images to an **existing** quickstart deployment. It does not create resources or deploy infrastructure changes. The workflow has local mock tests but has not been validated end-to-end in Azure. The older [Azure DevOps pipeline](azure-pipelines.yml) is not compatible with the current private SQL bootstrap and should not be used without changes.

The SQL template requests the free offer with auto-pause on free-limit exhaustion; once the allowance is used, a database is unavailable until the next month. The apps have at least one replica each, and readiness probes and background maintenance can consume database compute even when idle. Do not assume ordinary idle auto-pause will occur. Registry, image builds, private endpoints, logging and other resources may incur charges; an Azure budget is an alert, not a spending cap. There is no verified whole-environment pause/resume procedure or guaranteed £20 monthly cost.

Products API key rotation is not zero-downtime: the services do not accept overlapping keys. Schedule a maintenance window and update the matching secret on both services. The GitHub workflow updates images only, not secrets. If an update fails, restore the same previously working secret to both services; do not roll back only one.

Eligibility API key rotation has the same limitation: coordinate matching secrets between Mortgage and Eligibility during a maintenance window. For standalone local processes, set matching `EligibilityService__ApiKey` values of at least 32 characters on both APIs and set `EligibilityService__BaseUrl` on Mortgage. See the [Eligibility HTTP examples](PD.Eligibility.Api/PD.Eligibility.Api.http).

Deployments are incremental: clearing `alertEmail` later does not remove a previously created alert or action group. Disable or delete those named resources explicitly in Azure if they are no longer wanted.

### Database migrations

Each database has a separate EF Core migration history and one initial migration:

| Database | Migration project | Owned data | Local migration runner |
| --- | --- | --- | --- |
| `mortgages` | [PD.Mortgages.Infrastructure](PD.Mortgages.Infrastructure/Persistence/Migrations) | Applications, tombstones and audit entries | Mortgage API |
| `products` | [PD.Products.Infrastructure](PD.Products.Infrastructure/Persistence/Migrations) | Product catalog and seed data | Products API |
| `customer-sessions` | [PD.Mortgages.CustomerSessions](PD.Mortgages.CustomerSessions/Migrations) | Distributed customer session cache | Customer web app |

The session cache does not exist in the mortgage database. The Azure bootstrap job applies all three migrations with its admin identity before new app revisions start; runtime identities have only the required data roles. The GitHub workflow reruns the job when the bootstrap project or its dependencies change. Migration histories are independent: do not copy tables or migration history between databases.

All three migration histories were reset for a fresh deployment. Existing databases with the previous histories cannot apply these new initial migrations over their existing tables. The migration reset does **not** delete or update any database. For disposable local data, stop the apps, verify the server and database names, then use a SQL administration tool to drop only the three affected databases (`mortgages`, `products`, and `customer-sessions`) before restarting Aspire. This permanently deletes their data, audit history and customer sessions; product seed data is recreated. Do not delete the shared SQL volume or unrelated databases. If any data must be retained, back it up and plan a tested migration instead of dropping databases. Anonymous customers cannot recover drafts without their original session. Azure resources have not been deployed or validated by this repository alone; do not apply this reset to an existing Azure database without a data migration plan.

Future schema changes must use expand-and-contract migrations: first deploy a schema compatible with both the current and new app revisions, then deploy the new code, and remove old columns only after old revisions are retired. The pipeline applies migrations before the new revisions; an incompatible migration can break the old revision and make an image rollback unsafe. Review migration and snapshot JSON compatibility before every deployment.

### Restore exercise

Practice with fictional data in a separate resource group before relying on recovery. Azure SQL point-in-time restore creates a **new** database; check the available restore window and expected cost in the Azure portal. Select a UTC recovery time and restore `mortgages` to a uniquely named exercise database on the same logical server, for example:

```bash
az sql db restore --resource-group "<exercise-resource-group>" --server "<sql-server>" \
  --name mortgages --dest-name mortgages-restore-exercise --time "<UTC-time>"
```

Use an authorized SQL client to verify the migration history, application and audit record counts and a fictional submitted application's data. Run a compatible API revision against the restored database in an isolated environment, with appropriate identity permissions, and verify a staff read. Do not point the running apps at the exercise database or overwrite the original. Document the recovery point, elapsed time and results; delete only the explicitly named exercise database when finished.

SQL restore does not restore document blobs or Data Protection keys. Independently plan, configure and test Blob backup/version recovery before storing any important files; this template does not enable a complete Blob recovery solution. Products and customer sessions have independent databases and restore points; restoring one does not make the others transactionally consistent. Lost sessions or keys prevent customers recovering their prior anonymous session. A real recovery requires a coordinated cutover plan and validation, not just this exercise. This procedure has not yet been executed in Azure.
