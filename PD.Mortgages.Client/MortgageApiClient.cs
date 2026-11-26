using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Application.Services;
using PD.Mortgages.Contracts;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Client;

public sealed class MortgageApiClient(HttpClient client, IMortgageApiCredentials credentials) : IMortgageApplicationService
{
    private const string Root = "api/v1/mortgages";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<MortgageApplicationDto> CreateAsync(CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ApplicationResponse>(HttpMethod.Post, Root, null, cancellationToken));

    public async Task<MortgageApplicationDto> CreateForStaffAsync(CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ApplicationResponse>(HttpMethod.Post, $"{Root}/staff", null, cancellationToken));

    public async Task<MortgageApplicationDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ApplicationResponse>(HttpMethod.Get, $"{Root}/{id}", null, cancellationToken));

    public async Task<MortgageApplicationDto> GetForStaffAsync(Guid id, CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ApplicationResponse>(HttpMethod.Get, $"{Root}/staff/{id}", null, cancellationToken));

    public async Task<MortgageApplicationAuditPageDto> GetAuditTrailForStaffAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<AuditPageResponse>(HttpMethod.Get, QueryHelpers.AddQueryString($"{Root}/staff/{id}/audit",
            new Dictionary<string, string?> { ["page"] = page.ToString(), ["pageSize"] = pageSize.ToString() }), null, cancellationToken));

    public async Task<MortgageApplicationSearchPageDto> SearchForStaffAsync(string? search, ApplicationStatus? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ApplicationPageResponse>(HttpMethod.Get, QueryHelpers.AddQueryString($"{Root}/staff",
            new Dictionary<string, string?> { ["search"] = search, ["status"] = status is { } value ? MortgageContractMapper.ToContract(value).ToString() : null, ["page"] = page.ToString(), ["pageSize"] = pageSize.ToString() }), null, cancellationToken));

    public Task DeleteAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Root}/{id}", null, cancellationToken, expectedVersion);

    public Task SaveApplicantAsync(Guid id, string firstName, string lastName, string email, string phoneNumber, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}/applicant", new SaveApplicantRequest(firstName, lastName, email, phoneNumber), cancellationToken, expectedVersion);

    public Task SaveQualificationAsync(Guid id, decimal propertyValue, decimal deposit, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}/qualification", new SaveQualificationRequest(propertyValue, deposit), cancellationToken, expectedVersion);

    public Task SaveFinancesAsync(Guid id, decimal annualIncome, decimal monthlyCommitments, EmploymentType employmentType, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}/finances", new SaveFinancesRequest(annualIncome, monthlyCommitments, MortgageContractMapper.ToContract(employmentType)), cancellationToken, expectedVersion);

    public Task SaveConsentsAsync(Guid id, bool creditCheckConsent, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}/consents", new SaveConsentsRequest(creditCheckConsent), cancellationToken, expectedVersion);

    public async Task<MortgageApplicationDto> GenerateDecisionInPrincipleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ApplicationResponse>(HttpMethod.Post, $"{Root}/{id}/decision", null, cancellationToken, expectedVersion));

    public Task SelectProductAsync(Guid id, string productId, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"{Root}/{id}/product", new SelectProductRequest(productId), cancellationToken, expectedVersion);

    public async Task<Guid> AddDocumentAsync(Guid id, string fileName, byte[] contents, long expectedVersion, CancellationToken cancellationToken = default) =>
        (await SendAsync<UploadDocumentResponse>(HttpMethod.Post, $"{Root}/{id}/documents", new UploadDocumentRequest(fileName, contents), cancellationToken, expectedVersion)).Id;

    public async Task<byte[]?> GetDocumentAsync(Guid id, Guid documentId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/{id}/documents/{documentId}");
        using var response = await SendRequestAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var problem = await ReadProblemAsync(response, cancellationToken);
            if (problem?.Type == MortgageApiProblems.DocumentNotFound) return null;
            if (problem?.Type == MortgageApiProblems.ApplicationNotFound)
                throw new KeyNotFoundException(problem.Detail ?? "The application was not found.");
            throw new HttpRequestException("The Mortgage API document endpoint was not found.", null, response.StatusCode);
        }
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public Task SubmitAsync(Guid id, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Root}/{id}/submit", null, cancellationToken, expectedVersion);

    public async Task<SubmissionReceiptDto> SubmitCustomerAsync(Guid id, long expectedVersion, Guid submissionKey, CancellationToken cancellationToken = default) =>
        MortgageContractMapper.ToApplication(await SendAsync<ReceiptResponse>(HttpMethod.Post, $"{Root}/{id}/customer-submission", new SubmitCustomerRequest(submissionKey), cancellationToken, expectedVersion));

    public async Task<SubmissionReceiptDto?> GetSubmissionReceiptAsync(Guid id, Guid submissionKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/{id}/submission-receipt");
        request.Headers.Add("X-Submission-Key", submissionKey.ToString("D"));
        using var response = await SendRequestAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var problem = await ReadProblemAsync(response, cancellationToken);
            if (problem?.Type == MortgageApiProblems.ReceiptNotFound) return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var receipt = await response.Content.ReadFromJsonAsync<ReceiptResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Mortgage API returned an empty receipt.");
        return MortgageContractMapper.ToApplication(receipt);
    }

    public async Task<IReadOnlyList<MortgageProductDto>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync<List<ProductResponse>>(HttpMethod.Get, $"{Root}/products", null, cancellationToken))
            .Select(MortgageContractMapper.ToApplication).ToList();

    public async Task<IReadOnlyList<MortgageProductDto>> GetEligibleProductsAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await SendAsync<List<ProductResponse>>(HttpMethod.Get, $"{Root}/{id}/eligible-products", null, cancellationToken))
            .Select(MortgageContractMapper.ToApplication).ToList();

    public async Task<MortgageProductDto?> GetProductAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/products/{Uri.EscapeDataString(id)}");
        using var response = await SendRequestAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var problem = await ReadProblemAsync(response, cancellationToken);
            if (problem?.Type == MortgageApiProblems.ProductNotFound) return null;
            throw new HttpRequestException("The Mortgage API product endpoint was not found.", null, response.StatusCode);
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Mortgage API returned an empty response.");
        return MortgageContractMapper.ToApplication(product);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken, long? expectedVersion = null)
    {
        using var request = CreateRequest(method, uri, body);
        AddVersion(request, expectedVersion);
        using var response = await SendRequestAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Mortgage API returned an empty response.");
    }

    private async Task SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken, long? expectedVersion = null)
    {
        using var request = CreateRequest(method, uri, body);
        AddVersion(request, expectedVersion);
        using var response = await SendRequestAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, object? body) =>
        new(method, uri) { Content = body is null ? null : JsonContent.Create(body, options: JsonOptions) };

    private static void AddVersion(HttpRequestMessage request, long? version)
    {
        if (version.HasValue)
            request.Headers.Add("X-Expected-Version", version.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await credentials.ApplyAsync(request, cancellationToken);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var problem = await ReadProblemAsync(response, cancellationToken);
        var message = problem?.Detail ?? $"The Mortgage API returned HTTP {(int)response.StatusCode}.";
        throw response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ArgumentException(message),
            HttpStatusCode.Forbidden when problem?.Type == MortgageApiProblems.ApplicationUnavailable => new UnauthorizedAccessException(message),
            HttpStatusCode.NotFound when problem?.Type == MortgageApiProblems.ApplicationNotFound => new KeyNotFoundException(message),
            HttpStatusCode.Conflict when problem?.Type == MortgageApiProblems.ConcurrencyConflict => new MortgageConcurrencyException(),
            HttpStatusCode.Conflict => new InvalidOperationException(message),
            _ => new HttpRequestException(message, null, response.StatusCode)
        };
    }

    private static async Task<ProblemDetails?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.Content.Headers.ContentType?.MediaType == "application/problem+json"
            ? await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, cancellationToken)
            : null;
}
