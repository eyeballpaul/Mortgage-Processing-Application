namespace PD.Mortgages.Web.Services;

public interface IWizardSession
{
    Guid? CurrentApplicationId { get; }
    int CurrentStep { get; }
    void SetApplication(Guid applicationId, int step);
    void SetStep(int step);
    void Clear();
}
