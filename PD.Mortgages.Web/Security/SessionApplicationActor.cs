using PD.Mortgages.Application.Security;
using PD.Mortgages.Web.Services;

namespace PD.Mortgages.Web.Security;

public sealed class SessionApplicationActor(IWizardSession wizardSession) : IApplicationActor
{
    public bool IsAuthenticated => false;
    public string SubjectId => wizardSession.ActorId;
    public bool IsStaff => false;
}
