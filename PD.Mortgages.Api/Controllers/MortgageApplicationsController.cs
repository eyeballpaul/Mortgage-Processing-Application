using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Contracts;

namespace PD.Mortgages.Api.Controllers;

[ApiController]
[Route("api/v1/mortgages")]
[Authorize(Policy = "MortgageCaller")]
public sealed class MortgageApplicationsController(IMortgageApplicationService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApplicationResponse>> Create(CancellationToken cancellationToken)
    {
        var application = await service.CreateAsync(cancellationToken);
        return Created($"/api/v1/mortgages/{application.Id}", MortgageContractMapper.ToContract(application));
    }

    [HttpPost("staff")]
    [Authorize(Policy = "MortgageStaff")]
    public async Task<ActionResult<ApplicationResponse>> CreateForStaff(CancellationToken cancellationToken)
    {
        var application = await service.CreateForStaffAsync(cancellationToken);
        return Created($"/api/v1/mortgages/staff/{application.Id}", MortgageContractMapper.ToContract(application));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApplicationResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(MortgageContractMapper.ToContract(await service.GetAsync(id, cancellationToken)));

    [HttpGet("staff/{id:guid}")]
    [Authorize(Policy = "MortgageStaff")]
    public async Task<ActionResult<ApplicationResponse>> GetForStaff(Guid id, CancellationToken cancellationToken) =>
        Ok(MortgageContractMapper.ToContract(await service.GetForStaffAsync(id, cancellationToken)));

    [HttpGet("staff")]
    [Authorize(Policy = "MortgageStaff")]
    public async Task<ActionResult<ApplicationPageResponse>> Search(
        [FromQuery] string? search, [FromQuery] ApplicationStatusContract? status, [FromQuery] int page,
        [FromQuery] int pageSize, CancellationToken cancellationToken) =>
        Ok(MortgageContractMapper.ToContract(await service.SearchForStaffAsync(search,
            status is { } value ? MortgageContractMapper.ToApplication(value) : null, page, pageSize, cancellationToken)));

    [HttpGet("staff/{id:guid}/audit")]
    [Authorize(Policy = "MortgageStaff")]
    public async Task<ActionResult<AuditPageResponse>> GetAudit(
        Guid id, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken) =>
        Ok(MortgageContractMapper.ToContract(await service.GetAuditTrailForStaffAsync(id, page, pageSize, cancellationToken)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/applicant")]
    public async Task<IActionResult> SaveApplicant(Guid id, SaveApplicantRequest request, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        await service.SaveApplicantAsync(id, request.FirstName, request.LastName, request.Email, request.PhoneNumber, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/qualification")]
    public async Task<IActionResult> SaveQualification(Guid id, SaveQualificationRequest request, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        await service.SaveQualificationAsync(id, request.PropertyValue, request.Deposit, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/finances")]
    public async Task<IActionResult> SaveFinances(Guid id, SaveFinancesRequest request, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        var employmentType = MortgageContractMapper.ToApplication(request.EmploymentType);
        await service.SaveFinancesAsync(id, request.AnnualIncome, request.MonthlyCommitments, employmentType, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/consents")]
    public async Task<IActionResult> SaveConsents(Guid id, SaveConsentsRequest request, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        await service.SaveConsentsAsync(id, request.CreditCheckConsent, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/decision")]
    public async Task<ActionResult<ApplicationResponse>> GenerateDecision(Guid id, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken) =>
        Ok(MortgageContractMapper.ToContract(await service.GenerateDecisionInPrincipleAsync(id, expectedVersion!.Value, cancellationToken)));

    [HttpPut("{id:guid}/product")]
    public async Task<IActionResult> SelectProduct(Guid id, SelectProductRequest request, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        await service.SelectProductAsync(id, request.ProductId, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(7_100_000)]
    public async Task<ActionResult<UploadDocumentResponse>> AddDocument(Guid id, UploadDocumentRequest request, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        var documentId = await service.AddDocumentAsync(id, request.FileName, request.Contents, expectedVersion!.Value, cancellationToken);
        return Created($"/api/v1/mortgages/{id}/documents/{documentId}", new UploadDocumentResponse(documentId));
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> GetDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var contents = await service.GetDocumentAsync(id, documentId, cancellationToken);
        return contents is null
            ? NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Type = MortgageApiProblems.DocumentNotFound })
            : File(contents, "application/octet-stream");
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "MortgageStaff")]
    public async Task<IActionResult> Submit(Guid id, [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken cancellationToken)
    {
        await service.SubmitAsync(id, expectedVersion!.Value, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/customer-submission")]
    public async Task<ActionResult<ReceiptResponse>> SubmitCustomer(
        Guid id, SubmitCustomerRequest request,
        [FromHeader(Name = "X-Expected-Version"), Required, Range(0, long.MaxValue)] long? expectedVersion,
        CancellationToken cancellationToken) =>
        Ok(MortgageContractMapper.ToContract(await service.SubmitCustomerAsync(id, expectedVersion!.Value, request.SubmissionKey, cancellationToken)));

    [HttpGet("{id:guid}/submission-receipt")]
    public async Task<ActionResult<ReceiptResponse>> GetSubmissionReceipt(
        Guid id, [FromHeader(Name = "X-Submission-Key"), Required] Guid? submissionKey, CancellationToken cancellationToken)
    {
        var receipt = await service.GetSubmissionReceiptAsync(id, submissionKey!.Value, cancellationToken);
        return receipt is null
            ? NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Type = MortgageApiProblems.ReceiptNotFound })
            : Ok(MortgageContractMapper.ToContract(receipt));
    }

    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetProducts(CancellationToken cancellationToken) =>
        Ok((await service.GetProductsAsync(cancellationToken)).Select(MortgageContractMapper.ToContract).ToList());

    [HttpGet("{id:guid}/eligible-products")]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetEligibleProducts(Guid id, CancellationToken cancellationToken) =>
        Ok((await service.GetEligibleProductsAsync(id, cancellationToken)).Select(MortgageContractMapper.ToContract).ToList());

    [HttpGet("products/{id}")]
    public async Task<ActionResult<ProductResponse>> GetProduct(string id, CancellationToken cancellationToken)
    {
        var product = await service.GetProductAsync(id, cancellationToken);
        return product is null
            ? NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Type = MortgageApiProblems.ProductNotFound })
            : Ok(MortgageContractMapper.ToContract(product));
    }
}
