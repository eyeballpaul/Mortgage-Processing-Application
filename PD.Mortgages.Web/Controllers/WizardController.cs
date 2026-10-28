using Microsoft.AspNetCore.Mvc;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Web.Models;

namespace PD.Mortgages.Web.Controllers;

[Route("application")]
public sealed class WizardController(IMortgageApplicationService applicationService) : Controller
{
    private const string ApplicationSessionKey = "CurrentApplicationId";
    private const string StepSessionKey = "CurrentApplicationStep";
    private const long MaximumUploadBytes = 5 * 1024 * 1024;
    private const int MaximumDocumentCount = 10;
    private const long MaximumApplicationDocumentBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> PermittedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg"
    };

    [HttpGet("start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var application = await applicationService.CreateAsync(cancellationToken);
        HttpContext.Session.SetString(ApplicationSessionKey, application.Id.ToString());
        HttpContext.Session.SetInt32(StepSessionKey, 1);
        return RedirectToAction(nameof(Qualification));
    }

    [HttpPost("start-over")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartOver(CancellationToken cancellationToken)
    {
        if (CurrentApplicationId() is Guid currentApplicationId)
        {
            await applicationService.DeleteAsync(currentApplicationId, cancellationToken);
        }

        var application = await applicationService.CreateAsync(cancellationToken);
        HttpContext.Session.SetString(ApplicationSessionKey, application.Id.ToString());
        HttpContext.Session.SetInt32(StepSessionKey, 1);
        TempData["Notice"] = "Your previous application was deleted. You can start again below.";
        return RedirectToAction(nameof(Qualification));
    }

    [HttpGet("resume")]
    public IActionResult Resume()
    {
        if (CurrentApplicationId() is null)
        {
            TempData["Notice"] = "There is no saved application in this browser yet.";
            return RedirectToAction("Index", "Home");
        }

        return RedirectToStep(HttpContext.Session.GetInt32(StepSessionKey) ?? 1);
    }

    [HttpPost("save-progress")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveProgress(int step)
    {
        if (CurrentApplicationId() is null)
        {
            return RedirectToAction("Index", "Home");
        }

        HttpContext.Session.SetInt32(StepSessionKey, Math.Clamp(step, 1, 8));
        TempData["Notice"] = "Your progress is saved on this device. You can return to this application later.";
        return RedirectToStep(Math.Clamp(step, 1, 8));
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");

        var product = application.SelectedProductId is null
            ? null
            : await applicationService.GetProductAsync(application.SelectedProductId, cancellationToken);
        var loanAmount = (application.PropertyValue ?? 0) - (application.Deposit ?? 0);
        return View(new DashboardViewModel
        {
            Application = application,
            Product = product,
            CurrentStep = HttpContext.Session.GetInt32(StepSessionKey) ?? 1,
            EstimatedMonthlyPayment = product is null
                ? null
                : MortgagePaymentCalculator.MonthlyPayment(loanAmount, product.InterestRate)
        });
    }

    [HttpGet("qualification")]
    public async Task<IActionResult> Qualification(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        SetStep(1);
        return View(new QualificationViewModel
        {
            PropertyValue = application.PropertyValue,
            Deposit = application.Deposit
        });
    }

    [HttpPost("qualification")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Qualification(QualificationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            SetStep(1);
            return View(model);
        }

        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        await applicationService.SaveQualificationAsync(application.Id, model.PropertyValue!.Value, model.Deposit!.Value, cancellationToken);
        SetStep(2);
        return RedirectToAction(nameof(Applicant));
    }

    [HttpGet("applicant")]
    public async Task<IActionResult> Applicant(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 2);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        SetStep(2);
        return View(new ApplicantViewModel
        {
            FirstName = application.FirstName ?? "",
            LastName = application.LastName ?? "",
            Email = application.Email ?? "",
            PhoneNumber = application.PhoneNumber ?? ""
        });
    }

    [HttpPost("applicant")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Applicant(ApplicantViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            SetStep(2);
            return View(model);
        }

        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        await applicationService.SaveApplicantAsync(application.Id, model.FirstName.Trim(), model.LastName.Trim(), model.Email.Trim(), model.PhoneNumber.Trim(), cancellationToken);
        SetStep(3);
        return RedirectToAction(nameof(Finances));
    }

    [HttpGet("finances")]
    public async Task<IActionResult> Finances(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 3);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        SetStep(3);
        return View(new FinancesViewModel
        {
            AnnualIncome = application.AnnualIncome,
            MonthlyCommitments = application.MonthlyCommitments,
            EmploymentType = application.EmploymentType
        });
    }

    [HttpPost("finances")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finances(FinancesViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            SetStep(3);
            return View(model);
        }

        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        await applicationService.SaveFinancesAsync(application.Id, model.AnnualIncome!.Value, model.MonthlyCommitments!.Value, model.EmploymentType!.Value, cancellationToken);
        SetStep(4);
        return RedirectToAction(nameof(Connections));
    }

    [HttpGet("connections")]
    public async Task<IActionResult> Connections(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 4);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        SetStep(4);
        return View(new ConnectionsViewModel
        {
            CreditCheckConsent = application.CreditCheckConsent,
            OpenBankingConsent = application.OpenBankingConsent
        });
    }

    [HttpPost("connections")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connections(ConnectionsViewModel model, CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 4);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        await applicationService.SaveConsentsAsync(application.Id, model.CreditCheckConsent, model.OpenBankingConsent, cancellationToken);
        SetStep(5);
        return RedirectToAction(nameof(DecisionInPrinciple));
    }

    [HttpGet("decision-in-principle")]
    public async Task<IActionResult> DecisionInPrinciple(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 5);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;

        application = application.DecisionInPrinciple is null
            ? await applicationService.GenerateDecisionInPrincipleAsync(application.Id, cancellationToken)
            : application;

        SetStep(5);
        return View(application);
    }

    [HttpPost("decision-in-principle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContinueFromDecisionInPrinciple(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        if (application.DecisionInPrinciple is null)
        {
            return RedirectToAction(nameof(DecisionInPrinciple));
        }

        SetStep(6);
        return RedirectToAction(nameof(Products));
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 6);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        SetStep(6);
        var products = await applicationService.GetEligibleProductsAsync(application.Id, cancellationToken);
        return View(new ProductsViewModel
        {
            SelectedProductId = application.SelectedProductId,
            LoanAmount = (application.PropertyValue ?? 0) - (application.Deposit ?? 0),
            DipOutcome = application.DecisionInPrinciple?.Outcome,
            Products = products
        });
    }

    [HttpPost("products")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Products(ProductsViewModel model, CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 6);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        var eligibleProducts = await applicationService.GetEligibleProductsAsync(application.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(model.SelectedProductId) ||
            eligibleProducts.All(product => product.Id != model.SelectedProductId))
        {
            ModelState.AddModelError(nameof(model.SelectedProductId), "Choose an available mortgage product to continue.");
        }

        if (!ModelState.IsValid)
        {
            model.Products = eligibleProducts;
            model.LoanAmount = (application.PropertyValue ?? 0) - (application.Deposit ?? 0);
            model.DipOutcome = application.DecisionInPrinciple?.Outcome;
            SetStep(6);
            return View(model);
        }

        await applicationService.SelectProductAsync(application.Id, model.SelectedProductId!, cancellationToken);
        SetStep(7);
        return RedirectToAction(nameof(Documents));
    }

    [HttpGet("documents")]
    public async Task<IActionResult> Documents(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 7);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        SetStep(7);
        return View(application);
    }

    [HttpPost("documents")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaximumUploadBytes + 1024)]
    public async Task<IActionResult> Documents(IFormFile? document, CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 7);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;

        if (document is null || document.Length == 0)
        {
            ModelState.AddModelError(nameof(document), "Choose a file to upload.");
        }
        else if (document.Length > MaximumUploadBytes)
        {
            ModelState.AddModelError(nameof(document), "Files must be 5 MB or smaller.");
        }
        else if (application.Documents.Count >= MaximumDocumentCount ||
                 application.Documents.Sum(existing => existing.SizeInBytes) + document.Length > MaximumApplicationDocumentBytes)
        {
            ModelState.AddModelError(nameof(document), "This application has reached its document limit (10 files or 25 MB total).");
        }
        else if (!PermittedExtensions.Contains(Path.GetExtension(document.FileName)))
        {
            ModelState.AddModelError(nameof(document), "Upload a PDF, PNG or JPEG file.");
        }
        else
        {
            await using var memoryStream = new MemoryStream();
            await document.CopyToAsync(memoryStream, cancellationToken);
            var contents = memoryStream.ToArray();
            if (!HasAllowedSignature(Path.GetExtension(document.FileName), contents))
            {
                ModelState.AddModelError(nameof(document), "The file content does not match its file type. Please choose a valid PDF, PNG or JPEG.");
            }
            else
            {
                var safeFileName = Path.GetFileName(document.FileName);
                await applicationService.AddDocumentAsync(application.Id, safeFileName, contents, cancellationToken);
                TempData["Notice"] = $"{safeFileName} was added to your application.";
                SetStep(7);
                return RedirectToAction(nameof(Documents));
            }
        }

        SetStep(7);
        return View(application);
    }

    [HttpGet("documents/{documentId:guid}")]
    public async Task<IActionResult> DownloadDocument(Guid documentId, CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var metadata = application.Documents.SingleOrDefault(document => document.Id == documentId);
        var contents = await applicationService.GetDocumentAsync(application.Id, documentId, cancellationToken);
        if (metadata is null || contents is null) return NotFound();
        var contentType = Path.GetExtension(metadata.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
        return File(contents, contentType, metadata.FileName);
    }

    [HttpGet("review")]
    public async Task<IActionResult> Review(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 8);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;
        SetStep(8);
        return await ReviewViewAsync(application, cancellationToken);
    }

    [HttpPost("submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(CancellationToken cancellationToken)
    {
        var application = await GetCurrentApplicationAsync(cancellationToken);
        if (application is null) return RedirectToAction("Index", "Home");
        var prerequisiteRedirect = RedirectIfPrerequisitesMissing(application, 8);
        if (prerequisiteRedirect is not null) return prerequisiteRedirect;

        try
        {
            await applicationService.SubmitAsync(application.Id, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await ReviewViewAsync(application, cancellationToken);
        }

        TempData["Notice"] = "Your application has been submitted. We’ll be in touch with your next steps.";
        return RedirectToAction(nameof(Dashboard));
    }

    private async Task<IActionResult> ReviewViewAsync(MortgageApplicationDto application, CancellationToken cancellationToken)
    {
        var product = application.SelectedProductId is null
            ? null
            : await applicationService.GetProductAsync(application.SelectedProductId, cancellationToken);
        var loanAmount = (application.PropertyValue ?? 0) - (application.Deposit ?? 0);
        return View("Review", new ReviewViewModel
        {
            Application = application,
            Product = product,
            EstimatedMonthlyPayment = product is null ? null : MortgagePaymentCalculator.MonthlyPayment(loanAmount, product.InterestRate)
        });
    }

    private async Task<MortgageApplicationDto?> GetCurrentApplicationAsync(CancellationToken cancellationToken)
    {
        var id = CurrentApplicationId();
        if (id is null) return null;
        try
        {
            return await applicationService.GetAsync(id.Value, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            HttpContext.Session.Remove(ApplicationSessionKey);
            HttpContext.Session.Remove(StepSessionKey);
            return null;
        }
    }

    private Guid? CurrentApplicationId() =>
        Guid.TryParse(HttpContext.Session.GetString(ApplicationSessionKey), out var id) ? id : null;

    private void SetStep(int step) => HttpContext.Session.SetInt32(StepSessionKey, step);

    private IActionResult? RedirectIfPrerequisitesMissing(MortgageApplicationDto application, int requestedStep)
    {
        if (requestedStep > 1 && (application.PropertyValue is null || application.Deposit is null))
        {
            return RedirectToStep(1);
        }

        if (requestedStep > 2 &&
            (string.IsNullOrWhiteSpace(application.FirstName) ||
             string.IsNullOrWhiteSpace(application.LastName) ||
             string.IsNullOrWhiteSpace(application.Email) ||
             string.IsNullOrWhiteSpace(application.PhoneNumber)))
        {
            return RedirectToStep(2);
        }

        if (requestedStep > 3 && (application.AnnualIncome is null || application.EmploymentType is null))
        {
            return RedirectToStep(3);
        }

        if (requestedStep > 4 && !application.ConsentsRecorded)
        {
            return RedirectToStep(4);
        }

        if (requestedStep > 5 && application.DecisionInPrinciple is null)
        {
            return RedirectToStep(5);
        }

        if (requestedStep > 6 && application.SelectedProductId is null)
        {
            return RedirectToStep(6);
        }

        if (requestedStep == 8 && (HttpContext.Session.GetInt32(StepSessionKey) ?? 0) < 7)
        {
            return RedirectToStep(7);
        }

        return null;
    }

    private IActionResult RedirectToStep(int step) => step switch
    {
        2 => RedirectToAction(nameof(Applicant)),
        3 => RedirectToAction(nameof(Finances)),
        4 => RedirectToAction(nameof(Connections)),
        5 => RedirectToAction(nameof(DecisionInPrinciple)),
        6 => RedirectToAction(nameof(Products)),
        7 => RedirectToAction(nameof(Documents)),
        8 => RedirectToAction(nameof(Review)),
        _ => RedirectToAction(nameof(Qualification))
    };

    private static bool HasAllowedSignature(string extension, byte[] contents)
    {
        var header = contents.AsSpan();
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => header.StartsWith("%PDF-"u8),
            ".png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            _ => false
        };
    }
}
