namespace PD.Mortgages.Application.Security;

public interface IApplicationActor
{
    string? SubjectId { get; }
    bool IsAuthenticated { get; }
    bool IsStaff { get; }
}
