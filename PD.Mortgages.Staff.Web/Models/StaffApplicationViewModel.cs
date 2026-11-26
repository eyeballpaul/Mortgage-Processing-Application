using System.ComponentModel.DataAnnotations;
using PD.Mortgages.Application.Models;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Staff.Web.Models;

public sealed class StaffApplicationViewModel : IValidatableObject
{
    public Guid? Id { get; set; }
    public ApplicationStatus? Status { get; set; }
    public string? CreatedByEmployeeId { get; set; }

    [Required, StringLength(60, MinimumLength = 2)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = "";

    [Required, StringLength(60, MinimumLength = 2)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = "";

    [Required, Phone, StringLength(30)]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = "";

    [Required, Range(50000, 5000000, ErrorMessage = "Enter a property value between £50,000 and £5,000,000.")]
    [Display(Name = "Property value")]
    public decimal? PropertyValue { get; set; }

    [Required, Range(0, 5000000, ErrorMessage = "Enter a deposit of £0 or more.")]
    [Display(Name = "Deposit")]
    public decimal? Deposit { get; set; }

    [Required, Range(10000, 10000000, ErrorMessage = "Enter annual income between £10,000 and £10,000,000.")]
    [Display(Name = "Annual household income")]
    public decimal? AnnualIncome { get; set; }

    [Required, Range(0, 100000, ErrorMessage = "Enter monthly commitments between £0 and £100,000.")]
    [Display(Name = "Monthly commitments")]
    public decimal? MonthlyCommitments { get; set; }

    [Required]
    [Display(Name = "Employment status")]
    public EmploymentType? EmploymentType { get; set; }

    [Display(Name = "Credit-check consent recorded")]
    public bool CreditCheckConsent { get; set; }

    [Display(Name = "Selected mortgage product")]
    public string? SelectedProductId { get; set; }
    public MortgageProductDto? SelectedProduct { get; set; }

    public bool SubmitApplication { get; set; }
    public IReadOnlyList<MortgageProductDto> EligibleProducts { get; set; } = [];
    public DecisionInPrincipleDto? DecisionInPrinciple { get; set; }
    public IReadOnlyList<MortgageDocumentDto> Documents { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PropertyValue.HasValue && Deposit.HasValue && Deposit >= PropertyValue)
        {
            yield return new ValidationResult("The deposit must be less than the property value.", [nameof(Deposit)]);
        }

        if (SubmitApplication && string.IsNullOrWhiteSpace(SelectedProductId))
        {
            yield return new ValidationResult("Select an eligible product before submitting the application.", [nameof(SelectedProductId)]);
        }
    }
}
