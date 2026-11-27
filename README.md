# PD Mortgages

A .NET 10 demonstration of a UK mortgage submission journey. The figures, products and decisions are fictional: no lender or credit-check integrations are connected. Do not use real customer data.

The customer and staff journeys record an optional credit-check choice. Declining it does not prevent an application from continuing.

## Start here

1. **Run locally:** follow [Local development with Aspire](#local-development-with-aspire).
2. **Deploy to Azure for the first time:** follow the
   [setup-from-scratch guide](docs/azure-setup-from-scratch.md) in order.
3. **Create or remove an already configured Azure prototype:** use the
   [on-demand workflow instructions](AZURE_PROTOTYPE_GUIDE.md#run-the-workflows).
4. **Configure on-demand workflows:** follow the
   [one-time setup guide](docs/azure-on-demand-setup.md).
5. **Use the manual quickstart or troubleshoot notifications:** see the
   [advanced deployment guide](docs/azure-advanced-deployment.md).
6. **Understand the components:** read the [architecture guide](docs/architecture.md).
7. **Report a security issue:** follow the private
   [security reporting instructions](SECURITY.md).

Azure resources cost money while they exist. A budget sends alerts; it does not
stop charges. Deleting prototype resources also permanently deletes their data.

## Project layout

The [solution](PD.Mortgages.slnx) groups projects by Mortgages, Products,
Eligibility, Notifications and Hosting. These are solution folders for
navigation; the individual project folders remain at the repository root.
In the Mortgages group, the customer and staff web projects are
[PD.Mortgages.Customer.Web](PD.Mortgages.Customer.Web/) and
[PD.Mortgages.Staff.Web](PD.Mortgages.Staff.Web/).
The customer project's assembly and source namespace remain
`PD.Mortgages.Web` to preserve its existing application identity and telemetry.
Azure app and image names are unchanged. See the
[project grouping](docs/architecture.md#project-grouping) for what each group contains.

## Architecture

The customer and staff MVC apps call the internal **Mortgage API**; neither connects to the mortgage database or document storage. The API owns the application service, mortgage and audit data and document storage, and calls the internal Products and stateless Eligibility APIs. The customer app owns a separate SQL database for its distributed session cache. The staff app has no database because it has no independent data to persist. Both apps use separate Blob-backed Data Protection key stores in Azure.

In Azure, the main connections are:

```text
Customer browser -> Customer web -> Mortgage API -> mortgage SQL + document Blob
                         |               |    \-> Service Bus queue -> Notifications worker -> notifications SQL
                  customer sessions SQL  -> Products API -> products SQL
                                         -> Eligibility API (no database)
Staff browser -> Staff web -------------> Mortgage API
                     |
                     -> Microsoft Entra ID
```

Aspire uses a development-only customer credential, local document files and,
by default, a local Service Bus emulator. See the separate local and Azure
diagrams in the [architecture guide](docs/architecture.md).

For SQL-backed submissions, the Mortgage API commits an `ApplicationSubmitted` outbox entry with the application and audit entry. Its publisher sends the event to an Azure Service Bus Basic queue. A separate Notifications Worker records a simulation in its own database. **No email is sent.** Delivery is at least once; duplicate messages are deduplicated when recording the simulation. See [architecture.md](docs/architecture.md) and the [Azure deployment guide](docs/azure-advanced-deployment.md#submission-notifications) for limitations and operation.

Customer submissions are anonymous. A random server-side session actor owns each draft; the customer cannot reopen it after submission. A submission can be safely retried using the same session-bound attempt key and application version; a minimal reference and submission time can be recovered for 24 hours in the same session. Losing the session prevents recovery. Staff must sign in with Entra and have the `MortgageStaff` role. The API validates both the calling app and the delegated staff identity, scope and role; audit entries use the validated tenant/object IDs. In Azure the customer app uses a managed identity with the API's `MortgageCustomer` application role. Browser-supplied identity or role headers are not accepted as staff identity.

Starting or restarting a customer application requires an anti-forgery-protected POST and is limited to five attempts per observed remote address per ten minutes per web replica. Behind a reverse proxy, this may be the proxy's address, so multiple customers can share a limit. This in-process limit is a basic demo safeguard, not a distributed abuse-prevention boundary; use a trusted edge rate limiter for production.

In-progress customer and staff drafts expire after 30 days without authorized customer or staff activity; rejected reads do not extend retention, and submitted applications are not expired. Deletion or expiry records an audit event and leaves a minimal tombstone without the application details. While the Mortgage API is running, it retries document cleanup in the background and rechecks tombstones daily to remove any late-arriving uploads; cleanup waits if the API is stopped. Audit events and tombstones are retained; choose an appropriate retention policy before using real data.

An uncertain document upload or database commit retains the uploaded content instead of risking deletion of a committed document. The API logs the application and document IDs for investigation. Orphaned content for active applications is **not automatically reconciled**: inspect these warnings, confirm the final application metadata and wait for any in-flight operations before manually removing unreferenced blobs. Do not rely on this prototype for important documents without durable upload reconciliation and tested Blob recovery.

See [architecture.md](docs/architecture.md) for boundaries, local and Azure hosting, security and limitations.

## Local development with Aspire

### 1. Prepare your computer

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
   and [Docker Desktop](https://www.docker.com/products/docker-desktop/).
2. Start Docker Desktop and wait until it reports that it is running.
3. Open this repository in VS Code and select **Terminal → New Terminal**.
   Run `dotnet --version` and `docker --version` to check the tools. If a
   command is not found, install or start the relevant tool before continuing.
4. Trust the ASP.NET Core development certificate:

   ```bash
   dotnet dev-certs https --trust
   ```

   Follow the operating system's trust prompt. This allows local HTTPS.
5. Local notification processing starts an emulator and a separate SQL Server
   container. Read the [Service Bus emulator license](https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt)
   and [SQL Server license](https://go.microsoft.com/fwlink/?LinkId=746388).
   If you accept both, record your acknowledgement on this computer:

   ```bash
   dotnet user-secrets set LocalNotifications:AcceptEmulatorLicense true --project PD.Mortgages.AppHost
   ```

   Run this from the repository root. You should see `Successfully saved`.
   It saves the acknowledgement in your local .NET user-secrets store, not
   in the repository. You do not need to repeat it for each run on this
   computer under the same user account. If you do not accept both licenses, use the
   [opt-out instructions](#local-notification-processing) instead.

### 2. Configure sign-in

Follow [Configure Entra for staff and the API](#configure-entra-for-staff-and-the-api).
This requires an Entra tenant and an administrator who can grant consent.
Without that setup, staff sign-in will not work.

### 3. Start and check the apps

1. The local SQL volume persists between runs. Starting the default emulator
   processes **all pending local outbox events**, including older submissions.
   Use fictional data; if you do not want those events processed, use the
   [opt-out instructions](#local-notification-processing).
2. In the terminal, ensure the current folder contains
   `PD.Mortgages.slnx`. Then run:

   ```bash
   dotnet run --project PD.Mortgages.AppHost --launch-profile https
   ```

3. Open the Aspire dashboard URL printed in the terminal. It is a local page
   showing the apps, their status and logs. Wait for the resources to start;
   the first run may download images and take longer. Use the dashboard links
   to open the customer and staff apps. Sign in to Staff Web with an assigned account.
4. Try a customer-to-staff journey using **fictional data only**. To stop the
   local apps, return to the terminal and press **Ctrl+C**. Local SQL data is
   kept in a Docker volume, so stopping the apps does not erase it.

Aspire starts both web apps, the internal Mortgage, Products and Eligibility APIs, the Notifications Worker, and a local Service Bus emulator. One SQL Server container has separate `mortgages`, `products`, `customer-sessions` and `notifications` database resources; a second SQL Server container supports the emulator. Aspire generates separate, process-scoped keys for Mortgage's calls to Products and Eligibility; no local service key needs to be configured manually.

Aspire runs the .NET apps locally and the SQL servers and emulator in Docker. For a container image, build from the repository root using the relevant service Dockerfile, for example `docker build -f PD.Mortgages.Customer.Web/Dockerfile .`. Azure deployment uses six service images and a separate database-bootstrap image.

The Mortgage API stores documents under `LocalApplicationData/PD.Mortgages`; the SQL Docker volume persists data across restarts. The APIs migrate the mortgage and product databases locally at startup, and the customer web app migrates the customer-session database. Aspire creates a fresh, process-scoped development credential for customer-to-API calls; it is accepted **only** by an API running in Development. Staff still need real Entra sign-in and a delegated API token.

### Local notification processing

By default, Aspire starts the emulator and enables the Mortgage API publisher
and Notifications Worker in Development. The worker migrates its local
notifications database before starting. The emulator and its SQL dependency
need extra memory and disk space and may need to download container images.
To check the flow:

1. In the Aspire dashboard, wait for the local SQL servers, emulator, API and
   worker to start. Open the customer app from its dashboard link.
2. Complete and submit an application using fictional data. A successful
   submission can appear before its notification is processed.
3. Return to the dashboard, find `notifications-worker` in the resources
   list and open its console logs. Look for `Simulated notification recorded`.
   The publisher polls roughly every 30 seconds; **no email is sent**.
4. Stop the local apps with **Ctrl+C** in the terminal running Aspire when
   finished.

To start without the emulator for one run, use this Bash or zsh command:

```bash
LocalNotifications__UseEmulator=false dotnet run --project PD.Mortgages.AppHost --launch-profile https
```

In PowerShell, set `$env:LocalNotifications__UseEmulator = 'false'` before
running the normal command, then run `Remove-Item Env:LocalNotifications__UseEmulator`
after stopping. While disabled, SQL-backed submissions still create pending
outbox events; these will be processed when the emulator is enabled again.

The emulator uses a local connection string and is restricted by the apps to
Development and a loopback address. It does not connect to the Azure queue or
verify Azure Entra permissions, networking or scale-to-zero. The emulator
does not retain queued messages across container restarts: a message already
marked published in SQL can be lost if the emulator stops before processing.
It is a development aid, not a durable queue or production-equivalent test.
The emulator's default message lifetime and retry count also differ from the
Azure queue, so test expiry, retries and dead-letter handling in Azure separately.

#### If local notifications do not start

1. If the terminal asks for license acknowledgement, review both licenses
   and follow [Prepare your computer](#1-prepare-your-computer), or use the
   opt-out command above. Do not set the acknowledgement for someone else.
2. If a container fails to start, check Docker Desktop is running. In the
   Aspire dashboard, open the failed resource's logs and check for the first
   error. The emulator and its additional SQL container may need extra memory,
   disk space and time to start.
3. If an application submits but no notification appears, wait at least
   30 seconds, then check both `mortgages-api` and `notifications-worker`
   logs in the Aspire dashboard. Verify the emulator is running and that the
   opt-out setting is not active. A failed publisher or worker logs an error;
   correct it before retrying with a fictional application.
4. Do not delete the SQL volume to troubleshoot: it contains local application
   and audit data. If asking for help, share only the relevant error message
   after removing secrets and personal data.

### Configure Entra for staff and the API

Use separate development and Azure registrations in one tenant. For the
portal clicks to create the API and staff registrations, scope, roles and
user assignments, follow [steps 2 and 3 of the setup guide](docs/azure-setup-from-scratch.md#2-create-the-mortgage-api-registration).
For local development, also open Staff Web → **Authentication → Add a
platform → Web** and add `https://localhost:7241/signin-oidc`; add
`https://localhost:7241/signout-callback-oidc` as a Web redirect URI. Save.
The quickstart adds separate Azure callback URIs.

For each environment, check the following:

1. Verify the single-tenant **Mortgage API** registration has Application ID URI `api://<mortgage-api-client-id>`, the enabled delegated scope `Mortgage.Staff`, and enabled app roles with values exactly `MortgageStaff` (Users/Groups) and `MortgageCustomer` (Applications). For a new registration, set `api.requestedAccessTokenVersion` to `2` in its manifest; see the setup guide's caution before changing an existing registration. Ensure the corresponding enterprise application exists.
2. Verify the single-tenant **staff web** registration has the local Web redirects, a client secret and an enabled Users/Groups app role with value exactly `MortgageStaff`. The role value, not just its display name, must match.
3. Verify the staff registration has the Mortgage API's delegated `Mortgage.Staff` permission with administrator consent. Assign each staff user or group to `MortgageStaff` on **both** enterprise applications: the staff web role controls sign-in, and the API role appears in its access tokens. If assignment is required, the Azure customer managed identity also needs the API's `MortgageCustomer` role.
4. Configure the staff app and Aspire from the repository root. Replace the
   placeholders, without the brackets, using each registration's **Application
   (client) ID** and your tenant ID. Use the staff secret **value**, not its
   secret ID. Do not commit these values or share them in chat. The secret
   command can be recorded in shell history; use a secure terminal and follow
   your system's secret-handling policy. .NET user secrets are stored outside
   the repository but are **not encrypted**; use separate development
   credentials and secure your local account:

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

1. Open a terminal in the repository root.
2. Run:

   ```bash
   dotnet test PD.Mortgages.slnx
   ```

3. Check the final summary for failures. An actual staff sign-in still needs
   the Entra setup; passing tests alone does not prove Azure works.

Tests include API authorization, cross-session isolation, submission, staff audit attribution, document validation, draft lifecycle, concurrent edits, and migration shape. Test tokens are signed locally, not issued by Entra; an actual staff login and Azure deployment require the tenant setup above.

SQL notification integration tests are skipped unless `MORTGAGE_TEST_SQL_CONNECTION` points to a **disposable** SQL Server whose login can create and delete databases. They create and delete uniquely named test databases; do not point them at Azure or a server with important data. Never commit the connection string.

The additional emulator integration test is optional and is skipped unless
both connection variables are set:

1. Set up a **separate, disposable** SQL Server as described above and a
   **dedicated local emulator** with a `test-submission-notifications` queue.
   The queue is not part of the normal Aspire setup. See Microsoft's
   [emulator setup instructions](https://learn.microsoft.com/azure/service-bus-messaging/test-locally-with-service-bus-emulator)
   for creating a configured queue. Do not use the Azure namespace, normal
   local queue or a queue shared with another consumer: the test receives and
   completes messages.
2. In a secure terminal, set `MORTGAGE_TEST_SQL_CONNECTION` to the disposable
   SQL connection string and `MORTGAGE_TEST_SERVICEBUS_CONNECTION` to the
   dedicated emulator's local connection string. Do not commit or share either
   value. The test requires an emulator connection using a loopback address
   and `UseDevelopmentEmulator=true`.
3. From the repository root, run:

   ```bash
   dotnet test PD.Mortgages.Tests/PD.Mortgages.Tests.csproj --filter "FullyQualifiedName~SubmissionNotificationTests"
   ```

4. Check the test summary, stop the dedicated test resources, and remove the
   connection variables from your terminal session. The tests create and delete
   their own temporary databases; they do not clean up your emulator.

## Azure prototype deployment

The [setup-from-scratch guide](docs/azure-setup-from-scratch.md) gives step-by-step Azure, Entra and GitHub setup without secret values. The [prototype lifecycle guide](AZURE_PROTOTYPE_GUIDE.md) covers routine creation, deletion and recreation after resource-group deletion; the [advanced deployment guide](docs/azure-advanced-deployment.md) covers the interactive quickstart, costs and troubleshooting. The quickstart provisions one VNet with Container Apps and private-endpoint subnets, six Container Apps, four SQL databases, private SQL and Blob endpoints, a private database-bootstrap job and a public Service Bus Basic namespace. Both web apps have public HTTPS ingress; the three APIs have internal ingress and the worker has no ingress. SQL and Blob public network access is disabled. Staff access still requires Entra authentication and the staff app is not private-network-only. Use fictional data only; this is not a production security boundary.

The [GitHub Actions workflow](.github/workflows/ci.yml) tests pull requests and, after tests succeed on a push to `main`, deploys changed application images and the Service Bus notification resources and settings to an **existing** compatible quickstart deployment. It cannot create a missing worker or upgrade a legacy API-hosted consumer; follow the [legacy cutover](docs/azure-advanced-deployment.md#enable-on-an-existing-quickstart-deployment). After one-time setup, separate [on-demand workflows](AZURE_PROTOTYPE_GUIDE.md#on-demand-github-workflows) create the prototype or delete its recognized resources while retaining its resource group and identities. Earlier versions ran in Azure, but the separate worker and its scale rule have not yet been tested live. The older [Azure DevOps pipeline](azure-pipelines.yml) is not compatible with the current private SQL bootstrap and should not be used without changes.

The SQL template requests the free offer with auto-pause on free-limit exhaustion; once the allowance is used, a database is unavailable until the next month. The five web/API apps have at least one replica each; the worker is configured to scale to zero but has not been validated in Azure. Readiness probes and background maintenance can consume database compute even when idle. Do not assume ordinary idle auto-pause will occur. Registry, image builds, private endpoints, logging and other resources may incur charges; an Azure budget is an alert, not a spending cap. There is no verified whole-environment pause/resume procedure or guaranteed £20 monthly cost.

Products API key rotation is not zero-downtime: the services do not accept overlapping keys. Schedule a maintenance window and update the matching secret on both services. The GitHub workflow does not update secrets. If an update fails, restore the same previously working secret to both services; do not roll back only one.

Eligibility API key rotation has the same limitation: coordinate matching secrets between Mortgage and Eligibility during a maintenance window. For standalone local processes, set matching `EligibilityService__ApiKey` values of at least 32 characters on both APIs and set `EligibilityService__BaseUrl` on Mortgage. See the [Eligibility HTTP examples](PD.Eligibility.Api/PD.Eligibility.Api.http).

Deployments are incremental: clearing `alertEmail` later does not remove a previously created alert or action group. Disable or delete those named resources explicitly in Azure if they are no longer wanted.

### Database migrations

Each database has a separate EF Core migration history:

| Database | Migration project | Owned data | Local migration runner |
| --- | --- | --- | --- |
| `mortgages` | [PD.Mortgages.Infrastructure](PD.Mortgages.Infrastructure/Persistence/Migrations) | Applications, tombstones, audit, outbox and historical simulations | Mortgage API |
| `products` | [PD.Products.Infrastructure](PD.Products.Infrastructure/Persistence/Migrations) | Product catalog and seed data | Products API |
| `customer-sessions` | [PD.Mortgages.CustomerSessions](PD.Mortgages.CustomerSessions/Migrations) | Distributed customer session cache | Customer web app |
| `notifications` | [PD.Notifications.Worker](PD.Notifications.Worker/Migrations) | Simulated notification deduplication | Worker when the local emulator is enabled |

The session cache does not exist in the mortgage database. The Azure bootstrap job applies all four migrations with its admin identity before new app revisions start; runtime identities have only the required data roles. It also copies historical simulation records from the mortgage database idempotently, failing on conflicting event IDs. Migration histories remain independent and are not copied.

The original three migration histories were reset for a fresh deployment. Existing
databases with previous histories cannot apply these initial migrations over
their existing tables; the reset itself does not delete or update data.
For **disposable local data only**:

1. Stop Aspire with **Ctrl+C**. If any data must be retained, stop here and
   plan a tested backup and migration instead.
2. In your SQL administration tool, verify that you are connected to the
   local Docker SQL server, not Azure or another shared server.
3. Confirm the three database names: `mortgages`, `products` and
   `customer-sessions`. Dropping them permanently deletes their data, audit
   history and sessions. Do not delete the shared SQL volume or unrelated
   databases.
4. Drop only those three databases, then restart Aspire. The apps recreate
   them and seed products. Anonymous customers cannot recover drafts without
   their original session.

The successful prototype deployment does not validate this reset against
databases with previous migration histories. Do not apply it to an existing
Azure database without a data migration plan.

Future schema changes must use expand-and-contract migrations: first deploy a schema compatible with both the current and new app revisions, then deploy the new code, and remove old columns only after old revisions are retired. The pipeline applies migrations before the new revisions; an incompatible migration can break the old revision and make an image rollback unsafe. Review migration and snapshot JSON compatibility before every deployment.

### Restore exercise

Practice with fictional data in a separate resource group before relying on
recovery. Azure SQL point-in-time restore creates a **new** database and may
incur charges.

1. In Azure Portal, select the exercise subscription and resource group, then
   open the `mortgages` SQL database. Check the available restore window and
   expected cost.
2. Choose a UTC recovery time and a unique destination database name. In a
   terminal signed in to the same subscription, replace the placeholders and
   run:

   ```bash
   az sql db restore --resource-group "<exercise-resource-group>" --server "<sql-server>" \
     --name mortgages --dest-name mortgages-restore-exercise --time "<UTC-time>"
   ```

3. Use an authorized SQL client to check the restored migration history,
   application and audit counts, and a fictional submitted application's data.
4. In an isolated environment, run a compatible API revision with appropriate
   identity permissions and verify a staff read. Do not point the running apps
   at the exercise database or overwrite the original.
5. Record the recovery point, elapsed time and results. When finished, verify
   the exercise database name and delete **only** that database.

SQL restore does not restore document blobs or Data Protection keys. Independently plan, configure and test Blob backup/version recovery before storing any important files; this template does not enable a complete Blob recovery solution. Products and customer sessions have independent databases and restore points; restoring one does not make the others transactionally consistent. Lost sessions or keys prevent customers recovering their prior anonymous session. A real recovery requires a coordinated cutover plan and validation, not just this exercise. This procedure has not yet been executed in Azure.
