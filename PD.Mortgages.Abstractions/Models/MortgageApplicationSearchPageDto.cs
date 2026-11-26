namespace PD.Mortgages.Application.Models;

public sealed record MortgageApplicationSearchPageDto(
    IReadOnlyList<MortgageApplicationDto> Applications,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
