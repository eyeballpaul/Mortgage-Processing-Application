using System.ComponentModel.DataAnnotations;

namespace PD.Mortgages.Domain.Types.Enums;

public enum ApplicationStatus
{
    [Display(Name = "In progress")]
    InProgress,
    Submitted,
    [Display(Name = "Awaiting information")]
    AwaitingInformation,
    [Display(Name = "Approved in principle")]
    ApprovedInPrinciple
}
