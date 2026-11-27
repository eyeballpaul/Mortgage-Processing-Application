using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Types.Enums;
using PD.Mortgages.Staff.Web.Models;

namespace PD.Mortgages.Staff.Web.Controllers;

public sealed class StaffConcurrencyFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not MortgageConcurrencyException) return;
        context.ExceptionHandled = true;
        context.Result = new ContentResult
        {
            StatusCode = 409,
            ContentType = "text/html; charset=utf-8",
            Content = "This application was changed elsewhere. Your changes were not saved. Return to the application list and reopen it before trying again. <a href=\"/\">Application list</a>"
        };
    }
}

[Route("applications")]
[StaffConcurrencyFilter]
public sealed class ApplicationWizardController(
    IMortgageApplicationService applicationService,
    IDocumentUploadValidator documentUploadValidator) : Controller
{
    [HttpPost("start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var application = await applicationService.CreateForStaffAsync(cancellationToken);
        TempData["Notice"] = "A new application draft has been created.";
        return RedirectToAction(nameof(Step), new { id = application.Id, step = 1 });
    }

    [HttpGet("{id:guid}/step/{step:int}")]
    public async Task<IActionResult> Step(Guid id, int step, CancellationToken cancellationToken)
    {
        if (step is < 1 or > 8)
        {
            return NotFound();
        }

        var application = await GetApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.InProgress)
        {
            return RedirectToAction("Edit", "Applications", new { id });
        }

        var requiredStep = GetFirstIncompleteStep(application);
        if (step > requiredStep)
        {
            return RedirectToAction(nameof(Step), new { id, step = requiredStep });
        }

        return View("~/Views/Applications/Step.cshtml", CreateViewModel(application, step, await GetEligibleProductsAsync(id, step, cancellationToken)));
    }

    [HttpPost("{id:guid}/decision-in-principle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssessDecisionInPrinciple(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var application = await GetApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.InProgress)
        {
            return Conflict("Submitted applications are read-only.");
        }

        if (GetFirstIncompleteStep(application) < 5)
        {
            return Conflict("Complete the earlier application steps before running this assessment.");
        }

        var result = await applicationService.GenerateDecisionInPrincipleAsync(id, RequireVersion(expectedVersion), cancellationToken);
        Response.Headers["X-Application-Version"] = result.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return PartialView("~/Views/Applications/_DecisionInPrincipleResult.cshtml", result.DecisionInPrinciple
                ?? throw new InvalidOperationException("The assessment completed without producing a decision result."));
        }

        return RedirectToAction(nameof(Step), new { id, step = 5 });
    }

    [HttpPost("{id:guid}/step/5/continue")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContinueToMortgageOptions(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var application = await GetApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.InProgress)
        {
            return Conflict("Submitted applications are read-only.");
        }

        var requiredStep = GetFirstIncompleteStep(application);
        if (requiredStep < 5)
        {
            return RedirectToAction(nameof(Step), new { id, step = requiredStep });
        }

        if (application.DecisionInPrinciple is null)
        {
            await applicationService.GenerateDecisionInPrincipleAsync(id, RequireVersion(expectedVersion), cancellationToken);
        }

        return RedirectToAction(nameof(Step), new { id, step = 6 });
    }

    [HttpPost("{id:guid}/step/{step:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStep(Guid id, int step, StaffApplicationStepViewModel model, CancellationToken cancellationToken)
    {
        if (step is < 1 or > 8)
        {
            return NotFound();
        }

        var application = await GetApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.InProgress)
        {
            return Conflict("Submitted applications are read-only.");
        }

        var requiredStep = GetFirstIncompleteStep(application);
        if (step > requiredStep)
        {
            return RedirectToAction(nameof(Step), new { id, step = requiredStep });
        }

        model.Id = id;
        model.Step = step;
        if (!ModelState.IsValid || !ValidateStepModel(model, step))
        {
            return await RenderStepAsync(application, model, cancellationToken);
        }

        try
        {
            switch (step)
            {
                case 1:
                    await applicationService.SaveQualificationAsync(id, model.PropertyValue!.Value, model.Deposit!.Value, RequireVersion(model.ExpectedVersion), cancellationToken);
                    return RedirectToAction(nameof(Step), new { id, step = 2 });
                case 2:
                    await applicationService.SaveApplicantAsync(
                        id,
                        model.FirstName!.Trim(),
                        model.LastName!.Trim(),
                        model.Email!.Trim(),
                        model.PhoneNumber!.Trim(),
                        RequireVersion(model.ExpectedVersion),
                        cancellationToken);
                    return RedirectToAction(nameof(Step), new { id, step = 3 });
                case 3:
                    await applicationService.SaveFinancesAsync(
                        id,
                        model.AnnualIncome!.Value,
                        model.MonthlyCommitments!.Value,
                        model.EmploymentType!.Value,
                        RequireVersion(model.ExpectedVersion),
                        cancellationToken);
                    return RedirectToAction(nameof(Step), new { id, step = 4 });
                case 4:
                    await applicationService.SaveConsentsAsync(id, model.CreditCheckConsent, RequireVersion(model.ExpectedVersion), cancellationToken);
                    return RedirectToAction(nameof(Step), new { id, step = 5 });
                case 5:
                    application = await GetApplicationAsync(id, cancellationToken)
                        ?? throw new KeyNotFoundException($"Mortgage application '{id}' was not found.");
                    if (application.DecisionInPrinciple is null)
                    {
                        await applicationService.GenerateDecisionInPrincipleAsync(id, RequireVersion(model.ExpectedVersion), cancellationToken);
                    }

                    return RedirectToAction(nameof(Step), new { id, step = 6 });
                case 6:
                    var eligibleProducts = await applicationService.GetEligibleProductsAsync(id, cancellationToken);
                    if (string.IsNullOrWhiteSpace(model.SelectedProductId) ||
                        eligibleProducts.All(product => product.Id != model.SelectedProductId))
                    {
                        ModelState.AddModelError(nameof(model.SelectedProductId), "Choose an available mortgage product to continue.");
                        return await RenderStepAsync(application, model, cancellationToken);
                    }

                    await applicationService.SelectProductAsync(id, model.SelectedProductId, RequireVersion(model.ExpectedVersion), cancellationToken);
                    return RedirectToAction(nameof(Step), new { id, step = 7 });
                case 7:
                    return RedirectToAction(nameof(Step), new { id, step = 8 });
                case 8:
                    await applicationService.SubmitAsync(id, RequireVersion(model.ExpectedVersion), cancellationToken);
                    TempData["Notice"] = "The mortgage application was submitted.";
                    return RedirectToAction("Index", "Applications");
                default:
                    return NotFound();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var latest = await GetApplicationAsync(id, cancellationToken);
            return latest is null ? NotFound() : await RenderStepAsync(latest, model, cancellationToken);
        }
    }

    [HttpPost("{id:guid}/documents")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(DocumentUploadLimits.MaximumFileBytes + 1024)]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile? document, long? expectedVersion, CancellationToken cancellationToken)
    {
        var application = await GetApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.InProgress)
        {
            return Conflict("Submitted applications are read-only.");
        }

        using var contentStream = document?.OpenReadStream();
        var validation = await documentUploadValidator.ValidateAsync(
            document?.FileName,
            document?.Length ?? 0,
            contentStream,
            application.Documents.Count,
            application.Documents.Sum(item => item.SizeInBytes),
            cancellationToken);
        if (!validation.IsValid)
        {
            TempData["UploadError"] = validation.ErrorMessage;
            return RedirectToAction(nameof(Step), new { id, step = 7 });
        }

        await applicationService.AddDocumentAsync(id, validation.SafeFileName!, validation.Contents!, RequireVersion(expectedVersion), cancellationToken);
        TempData["Notice"] = "The document was added to the application.";
        return RedirectToAction(nameof(Step), new { id, step = 7 });
    }

    private async Task<IActionResult> RenderStepAsync(
        MortgageApplicationDto application,
        StaffApplicationStepViewModel model,
        CancellationToken cancellationToken)
    {
        var latest = await GetApplicationAsync(application.Id, cancellationToken) ?? application;
        var page = CreateViewModel(latest, model.Step, await GetEligibleProductsAsync(latest.Id, model.Step, cancellationToken));
        page.PropertyValue = model.PropertyValue;
        page.Deposit = model.Deposit;
        page.FirstName = model.FirstName;
        page.LastName = model.LastName;
        page.Email = model.Email;
        page.PhoneNumber = model.PhoneNumber;
        page.AnnualIncome = model.AnnualIncome;
        page.MonthlyCommitments = model.MonthlyCommitments;
        page.EmploymentType = model.EmploymentType;
        page.CreditCheckConsent = model.CreditCheckConsent;
        page.SelectedProductId = model.SelectedProductId;
        page.ExpectedVersion = model.ExpectedVersion;
        return View("~/Views/Applications/Step.cshtml", page);
    }

    private StaffApplicationStepViewModel CreateViewModel(
        MortgageApplicationDto application,
        int step,
        IReadOnlyList<MortgageProductDto> eligibleProducts) => new()
    {
        Id = application.Id,
        Step = step,
        ExpectedVersion = application.Version,
        Application = application,
        PropertyValue = application.PropertyValue,
        Deposit = application.Deposit,
        FirstName = application.FirstName,
        LastName = application.LastName,
        Email = application.Email,
        PhoneNumber = application.PhoneNumber,
        AnnualIncome = application.AnnualIncome,
        MonthlyCommitments = application.MonthlyCommitments,
        EmploymentType = application.EmploymentType,
        CreditCheckConsent = application.CreditCheckConsent,
        SelectedProductId = application.SelectedProductId,
        EligibleProducts = eligibleProducts,
        SelectedProduct = application.SelectedProduct
    };

    private Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(Guid id, int step, CancellationToken cancellationToken) =>
        step == 6 ? applicationService.GetEligibleProductsAsync(id, cancellationToken) : Task.FromResult<IReadOnlyList<MortgageProductDto>>([]);

    private static long RequireVersion(long? version) => version ?? throw new MortgageConcurrencyException();

    private async Task<MortgageApplicationDto?> GetApplicationAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await applicationService.GetForStaffAsync(id, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private bool ValidateStepModel(StaffApplicationStepViewModel model, int step)
    {
        switch (step)
        {
            case 1:
                if (model.PropertyValue is null) ModelState.AddModelError(nameof(model.PropertyValue), "Enter the property value.");
                if (model.Deposit is null) ModelState.AddModelError(nameof(model.Deposit), "Enter the customer's deposit.");
                if (model.PropertyValue.HasValue && model.Deposit.HasValue && model.Deposit >= model.PropertyValue)
                {
                    ModelState.AddModelError(nameof(model.Deposit), "The deposit must be less than the property value.");
                }
                break;
            case 2:
                if (string.IsNullOrWhiteSpace(model.FirstName)) ModelState.AddModelError(nameof(model.FirstName), "Enter the customer's first name.");
                if (string.IsNullOrWhiteSpace(model.LastName)) ModelState.AddModelError(nameof(model.LastName), "Enter the customer's last name.");
                if (string.IsNullOrWhiteSpace(model.Email)) ModelState.AddModelError(nameof(model.Email), "Enter the customer's email address.");
                if (string.IsNullOrWhiteSpace(model.PhoneNumber)) ModelState.AddModelError(nameof(model.PhoneNumber), "Enter the customer's phone number.");
                break;
            case 3:
                if (model.AnnualIncome is null) ModelState.AddModelError(nameof(model.AnnualIncome), "Enter the customer's annual income.");
                if (model.MonthlyCommitments is null) ModelState.AddModelError(nameof(model.MonthlyCommitments), "Enter the customer's monthly commitments.");
                if (model.EmploymentType is null) ModelState.AddModelError(nameof(model.EmploymentType), "Select the customer's employment status.");
                break;
        }

        return ModelState.IsValid;
    }

    private static int GetFirstIncompleteStep(MortgageApplicationDto application)
    {
        if (application.PropertyValue is null || application.Deposit is null) return 1;
        if (string.IsNullOrWhiteSpace(application.FirstName) || string.IsNullOrWhiteSpace(application.LastName) ||
            string.IsNullOrWhiteSpace(application.Email) || string.IsNullOrWhiteSpace(application.PhoneNumber)) return 2;
        if (application.AnnualIncome is null || application.MonthlyCommitments is null || application.EmploymentType is null) return 3;
        if (!application.ConsentsRecorded) return 4;
        if (application.DecisionInPrinciple is null) return 5;
        if (application.SelectedProductId is null) return 6;
        return 8;
    }

}
