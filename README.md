# PD Mortgages

A .NET 10 ASP.NET Core MVC demonstration of a UK mortgage application journey. It uses Razor views, Bootstrap 5, server-side DataAnnotations validation, session state, and in-memory repositories. No database or external financial integrations are required.

## Architecture

```text
PD.Mortgages.Web
  -> PD.Mortgages.Application
       -> PD.Mortgages.Domain
  -> PD.Mortgages.Infrastructure
       -> PD.Mortgages.Domain
```

- **Domain** contains the mortgage application aggregate, product and document entities, application enums, the `LoanToValue` value object, eligibility domain service, and repository/storage contracts. It has no project dependencies.
- **Application** contains DTOs and the application service that coordinates persistence, product eligibility, document storage, and submission.
- **Infrastructure** implements the domain contracts with thread-safe in-memory collections and realistic example mortgage products.
- **Web** contains the MVC controllers, annotated view models, strongly typed Razor pages, session configuration, layout, and responsive design system.

The application flow is: property and deposit → applicant details → income and commitments → optional credit/open-banking consent → eligible example products → optional document upload → review and submission. A dashboard summarizes status, timeline, product, and documents.

## Run

```bash
dotnet restore PD.Mortgages.slnx
dotnet build PD.Mortgages.slnx
dotnet run --project PD.Mortgages.Web
```

Open the local URL printed by `dotnet run`. Application progress and uploaded documents are held in memory and are cleared when the process restarts. Session cookies allow a customer to resume in the same browser while the process remains available.

### Run and debug in VS Code

Open the solution folder in VS Code and install the recommended C# Dev Kit extension. Press **F5** and choose **PD Mortgages (Development)** to build, launch the web app, and open it in your browser. Alternatively, use **Terminal → Run Task → run web app**. The VS Code launch and task configuration uses port `5085`.

## Demonstration limitations

PD Mortgages, its rates, products, estimates, and statuses are fictional. Credit checks, open banking, underwriting, and lender submission are not connected to external services. Uploaded PDFs and images are stored only in process memory with size/type checks; this is not a production document portal and must not be used for real financial or identity data.
