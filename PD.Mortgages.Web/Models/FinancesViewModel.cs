using System.ComponentModel.DataAnnotations;
using PD.Mortgages.Domain.Enums;

namespace PD.Mortgages.Web.Models;

public sealed class FinancesViewModel
{
    [Required, Range(10000, 10000000, ErrorMessage = "Enter an annual income between £10,000 and £10,000,000.")]
    [Display(Name = "Annual household income")]
    [DataType(DataType.Currency)]
    public decimal? AnnualIncome { get; set; }

    [Required, Range(0, 100000, ErrorMessage = "Enter monthly commitments between £0 and £100,000.")]
    [Display(Name = "Monthly credit commitments")]
    [DataType(DataType.Currency)]
    public decimal? MonthlyCommitments { get; set; }

    [Required]
    [Display(Name = "Employment status")]
    public EmploymentType? EmploymentType { get; set; }
}
