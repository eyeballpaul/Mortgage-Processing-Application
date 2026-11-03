using PD.Mortgages.Application.Models;

namespace PD.Mortgages.Web.Services;

public sealed class WizardPrerequisiteService : IWizardPrerequisiteService
{
    public int? GetMissingStep(MortgageApplicationDto application, int requestedStep, int currentStep)
    {
        if (requestedStep > 1 && (application.PropertyValue is null || application.Deposit is null))
        {
            return 1;
        }

        if (requestedStep > 2 &&
            (string.IsNullOrWhiteSpace(application.FirstName) ||
             string.IsNullOrWhiteSpace(application.LastName) ||
             string.IsNullOrWhiteSpace(application.Email) ||
             string.IsNullOrWhiteSpace(application.PhoneNumber)))
        {
            return 2;
        }

        if (requestedStep > 3 && (application.AnnualIncome is null || application.EmploymentType is null))
        {
            return 3;
        }

        if (requestedStep > 4 && !application.ConsentsRecorded)
        {
            return 4;
        }

        if (requestedStep > 5 && application.DecisionInPrinciple is null)
        {
            return 5;
        }

        if (requestedStep > 6 && application.SelectedProductId is null)
        {
            return 6;
        }

        return requestedStep == 8 && currentStep < 7 ? 7 : null;
    }
}
