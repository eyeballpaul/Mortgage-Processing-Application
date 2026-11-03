using PD.Mortgages.Application.Models;

namespace PD.Mortgages.Web.Services;

public interface IWizardPrerequisiteService
{
    int? GetMissingStep(MortgageApplicationDto application, int requestedStep, int currentStep);
}
