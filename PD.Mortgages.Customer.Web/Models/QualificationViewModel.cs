using System.ComponentModel.DataAnnotations;

namespace PD.Mortgages.Web.Models;

public sealed class QualificationViewModel : IValidatableObject
{
    [Display(Name = "Property value")]
    [Required]
    [Range(50000, 5000000, ErrorMessage = "Enter a property value between £50,000 and £5,000,000.")]
    [DataType(DataType.Currency)]
    public decimal? PropertyValue { get; set; }

    [Display(Name = "Your deposit")]
    [Required]
    [Range(0, 5000000, ErrorMessage = "Your deposit cannot be negative.")]
    [DataType(DataType.Currency)]
    public decimal? Deposit { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PropertyValue.HasValue && Deposit.HasValue && Deposit >= PropertyValue)
        {
            yield return new ValidationResult("Your deposit must be less than the property value.", [nameof(Deposit)]);
        }
    }
}
