namespace PD.Mortgages.Web.Services;

public interface IWizardSession
{
    Guid? CurrentApplicationId { get; }
    string ActorId { get; }
    int CurrentStep { get; }
    Guid? PendingSubmissionKey { get; }
    Guid? ReceiptApplicationId { get; }
    Guid? ReceiptKey { get; }
    Guid GetOrCreateSubmissionKey();
    void CompleteSubmission(Guid applicationId, Guid submissionKey);
    void ClearReceipt();
    void SetApplication(Guid applicationId, int step);
    void SetStep(int step);
    void Clear();
}
