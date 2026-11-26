using Microsoft.AspNetCore.Http;

namespace PD.Mortgages.Web.Services;

public sealed class WizardSession(IHttpContextAccessor httpContextAccessor) : IWizardSession
{
    private const string ApplicationSessionKey = "CurrentApplicationId";
    private const string StepSessionKey = "CurrentApplicationStep";
    private const string ActorSessionKey = "ApplicationSessionActor";
    private const string PendingSubmissionSessionKey = "PendingSubmissionKey";
    private const string ReceiptApplicationSessionKey = "ReceiptApplicationId";
    private const string ReceiptKeySessionKey = "ReceiptKey";

    private ISession Session => httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("An active HTTP session is required.");

    public Guid? CurrentApplicationId =>
        Guid.TryParse(Session.GetString(ApplicationSessionKey), out var id) ? id : null;

    public int CurrentStep => Session.GetInt32(StepSessionKey) ?? 1;
    public Guid? PendingSubmissionKey => ReadGuid(PendingSubmissionSessionKey);
    public Guid? ReceiptApplicationId => ReadGuid(ReceiptApplicationSessionKey);
    public Guid? ReceiptKey => ReadGuid(ReceiptKeySessionKey);

    public Guid GetOrCreateSubmissionKey()
    {
        if (PendingSubmissionKey is Guid key) return key;
        key = Guid.NewGuid();
        Session.SetString(PendingSubmissionSessionKey, key.ToString("D"));
        return key;
    }

    public void CompleteSubmission(Guid applicationId, Guid submissionKey)
    {
        Session.SetString(ReceiptApplicationSessionKey, applicationId.ToString("D"));
        Session.SetString(ReceiptKeySessionKey, submissionKey.ToString("D"));
        Session.Remove(ApplicationSessionKey);
        Session.Remove(StepSessionKey);
        Session.Remove(PendingSubmissionSessionKey);
    }

    public void ClearReceipt()
    {
        Session.Remove(ReceiptApplicationSessionKey);
        Session.Remove(ReceiptKeySessionKey);
    }

    private Guid? ReadGuid(string key) => Guid.TryParse(Session.GetString(key), out var value) ? value : null;

    public string ActorId
    {
        get
        {
            var actorId = Session.GetString(ActorSessionKey);
            if (actorId is null)
            {
                actorId = $"session:{Guid.NewGuid():N}";
                Session.SetString(ActorSessionKey, actorId);
            }

            return actorId;
        }
    }

    public void SetApplication(Guid applicationId, int step)
    {
        Session.SetString(ApplicationSessionKey, applicationId.ToString());
        Session.SetInt32(StepSessionKey, step);
        Session.Remove(PendingSubmissionSessionKey);
    }

    public void SetStep(int step) => Session.SetInt32(StepSessionKey, Math.Clamp(step, 1, 8));

    public void Clear()
    {
        Session.Remove(ApplicationSessionKey);
        Session.Remove(StepSessionKey);
        Session.Remove(ActorSessionKey);
        Session.Remove(PendingSubmissionSessionKey);
        ClearReceipt();
    }
}
