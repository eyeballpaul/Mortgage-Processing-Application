using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PD.Mortgages.Staff.Web.Security;

namespace PD.Mortgages.Tests;

public sealed class EntraStaffApplicationActorTests
{
    [Fact]
    public void StaffActorUsesTenantAndObjectIdsAsStableSubject()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var actor = CreateActor(
            new Claim("tid", tenantId.ToString()),
            new Claim("oid", objectId.ToString()),
            new Claim("roles", StaffAuthorization.StaffRole));

        Assert.True(actor.IsAuthenticated);
        Assert.True(actor.IsStaff);
        Assert.Equal($"{tenantId:D}:{objectId:D}", actor.SubjectId);
    }

    [Fact]
    public void AuthenticatedUserWithoutStaffRoleIsNotStaff()
    {
        var actor = CreateActor(
            new Claim("tid", Guid.NewGuid().ToString()),
            new Claim("oid", Guid.NewGuid().ToString()));

        Assert.True(actor.IsAuthenticated);
        Assert.False(actor.IsStaff);
    }

    private static EntraStaffApplicationActor CreateActor(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "test", "name", "roles");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new EntraStaffApplicationActor(new HttpContextAccessor { HttpContext = context });
    }
}
