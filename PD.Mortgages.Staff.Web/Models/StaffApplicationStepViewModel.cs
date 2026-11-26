using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Staff.Web.Models;

public sealed class StaffApplicationStepViewModel
{
    public Guid Id { get; set; }
    public int Step { get; set; }
    public long? ExpectedVersion { get; set; }

    [BindNever, ValidateNever]
    public MortgageApplicationDto Application { get; set; } = default!;

    [BindNever, ValidateNever]
    public IReadOnlyList<MortgageProductDto> EligibleProducts { get; set; } = [];

    [BindNever, ValidateNever]
    public MortgageProductDto? SelectedProduct { get; set; }

    [Range(50000, 5000000, ErrorMessage = "Enter a property value between £50,000 and £5,000,000.")]
    public decimal? PropertyValue { get; set; }

    [Range(0, 5000000, ErrorMessage = "Enter a deposit of £0 or more.")]
    public decimal? Deposit { get; set; }

    [StringLength(60, MinimumLength = 2)]
    public string? FirstName { get; set; }

    [StringLength(60, MinimumLength = 2)]
    public string? LastName { get; set; }

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }

    [Range(10000, 10000000, ErrorMessage = "Enter annual income between £10,000 and £10,000,000.")]
    public decimal? AnnualIncome { get; set; }

    [Range(0, 100000, ErrorMessage = "Enter monthly commitments between £0 and £100,000.")]
    public decimal? MonthlyCommitments { get; set; }

    public EmploymentType? EmploymentType { get; set; }
    public bool CreditCheckConsent { get; set; }
    public bool OpenBankingConsent { get; set; }
    public string? SelectedProductId { get; set; }

}
