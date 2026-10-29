using Microsoft.AspNetCore.Http;

namespace PD.Mortgages.Web.Services;

public sealed class WizardSession(IHttpContextAccessor httpContextAccessor) : IWizardSession
{
    private const string ApplicationSessionKey = "CurrentApplicationId";
    private const string StepSessionKey = "CurrentApplicationStep";

    private ISession Session => httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("An active HTTP session is required.");

    public Guid? CurrentApplicationId =>
        Guid.TryParse(Session.GetString(ApplicationSessionKey), out var id) ? id : null;

    public int CurrentStep => Session.GetInt32(StepSessionKey) ?? 1;

    public void SetApplication(Guid applicationId, int step)
    {
        Session.SetString(ApplicationSessionKey, applicationId.ToString());
        Session.SetInt32(StepSessionKey, step);
    }

    public void SetStep(int step) => Session.SetInt32(StepSessionKey, Math.Clamp(step, 1, 8));

    public void Clear()
    {
        Session.Remove(ApplicationSessionKey);
        Session.Remove(StepSessionKey);
    }
}
