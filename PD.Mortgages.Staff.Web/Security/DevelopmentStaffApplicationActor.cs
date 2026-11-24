using PD.Mortgages.Application.Security;

namespace PD.Mortgages.Staff.Web.Security;

public sealed class DevelopmentStaffApplicationActor : IApplicationActor
{
    public string? SubjectId => "development-staff";
    public bool IsAuthenticated => true;
    public bool IsStaff => true;
}
