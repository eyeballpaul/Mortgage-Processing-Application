using System.ComponentModel.DataAnnotations;

namespace PD.Mortgages.Domain.Enums;

public enum DecisionInPrincipleOutcome
{
    [Display(Name = "Credit check not selected")]
    CreditCheckNotSelected,
    [Display(Name = "Within indicative range")]
    WithinIndicativeRange,
    [Display(Name = "Above indicative range")]
    AboveIndicativeRange
}
