using Microsoft.AspNetCore.Mvc;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Domain.Enums;
using PD.Mortgages.Staff.Web.Models;

namespace PD.Mortgages.Staff.Web.Controllers;

[Route("")]
public sealed class ApplicationsController(IMortgageApplicationService applicationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, ApplicationStatus? status, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        var results = await applicationService.SearchForStaffAsync(search, status, page, pageSize, cancellationToken);
        var model = new StaffDashboardViewModel
        {
            Search = search,
            Status = status,
            Applications = results.Applications,
            TotalCount = results.TotalCount,
            Page = results.Page,
            PageSize = results.PageSize,
            TotalPages = results.TotalPages
        };
        return View(model);
    }

    [HttpGet("applications/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var application = await applicationService.GetForStaffAsync(id, cancellationToken);
            if (application.Status == ApplicationStatus.InProgress)
            {
                return RedirectToAction("Step", "ApplicationWizard", new { id, step = 1 });
            }

            var model = FromApplication(application);
            return View(model);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("applications/audit")]
    public IActionResult FindAudit(Guid? id)
    {
        if (!ModelState.IsValid || id is null || id == Guid.Empty)
            return BadRequest("Enter a valid full application ID.");
        return RedirectToAction(nameof(AuditTrail), new { id });
    }

    [HttpGet("applications/{id:guid}/audit")]
    public async Task<IActionResult> AuditTrail(Guid id, int page = 1, CancellationToken cancellationToken = default)
    {
        const int pageSize = 25;
        try
        {
            var audit = await applicationService.GetAuditTrailForStaffAsync(id, page, pageSize, cancellationToken);
            ApplicationStatus? status;
            try
            {
                status = (await applicationService.GetForStaffAsync(id, cancellationToken)).Status;
            }
            catch (KeyNotFoundException)
            {
                status = null;
            }
            return View(new StaffAuditTrailViewModel
            {
                ApplicationId = id,
                Status = status,
                Events = audit.Events,
                TotalCount = audit.TotalCount,
                Page = audit.Page,
                PageSize = audit.PageSize,
                TotalPages = audit.TotalPages
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("applications/{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var application = await applicationService.GetForStaffAsync(id, cancellationToken);
            var document = application.Documents.SingleOrDefault(item => item.Id == documentId);
            if (document is null)
            {
                return NotFound();
            }

            var contents = await applicationService.GetDocumentAsync(id, documentId, cancellationToken);
            return contents is null
                ? NotFound()
                : File(contents, "application/octet-stream", document.FileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static StaffApplicationViewModel FromApplication(MortgageApplicationDto application) => new()
    {
        Id = application.Id,
        Status = application.Status,
        CreatedByEmployeeId = application.CreatedByEmployeeId,
        FirstName = application.FirstName ?? string.Empty,
        LastName = application.LastName ?? string.Empty,
        Email = application.Email ?? string.Empty,
        PhoneNumber = application.PhoneNumber ?? string.Empty,
        PropertyValue = application.PropertyValue,
        Deposit = application.Deposit,
        AnnualIncome = application.AnnualIncome,
        MonthlyCommitments = application.MonthlyCommitments,
        EmploymentType = application.EmploymentType,
        CreditCheckConsent = application.CreditCheckConsent,
        OpenBankingConsent = application.OpenBankingConsent,
        SelectedProductId = application.SelectedProductId,
        SelectedProduct = application.SelectedProduct,
        DecisionInPrinciple = application.DecisionInPrinciple,
        Documents = application.Documents
    };
}
