using System.ComponentModel.DataAnnotations;

namespace PD.Mortgages.Web.Models;

public sealed class ApplicantViewModel
{
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
    [Display(Name = "Mobile number")]
    public string PhoneNumber { get; set; } = "";
}
