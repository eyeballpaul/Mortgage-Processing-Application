using System.ComponentModel.DataAnnotations;

namespace PD.Mortgages.Domain.Types.Enums;

public enum EmploymentType
{
    Employed,
    [Display(Name = "Self-employed")]
    SelfEmployed,
    Retired,
    Other
}
