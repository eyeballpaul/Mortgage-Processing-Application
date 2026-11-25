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
            new Claim(StaffAuthorization.TenantIdClaim, tenantId.ToString()),
            new Claim(StaffAuthorization.ObjectIdClaim, objectId.ToString()),
            new Claim(StaffAuthorization.RoleClaimType, StaffAuthorization.StaffRole));

        Assert.True(actor.IsAuthenticated);
        Assert.True(actor.IsStaff);
        Assert.Equal($"{tenantId:D}:{objectId:D}", actor.SubjectId);
    }

    [Fact]
    public void AuthenticatedUserWithoutStaffRoleIsNotStaff()
    {
        var actor = CreateActor(
            new Claim(StaffAuthorization.TenantIdClaim, Guid.NewGuid().ToString()),
            new Claim(StaffAuthorization.ObjectIdClaim, Guid.NewGuid().ToString()));

        Assert.True(actor.IsAuthenticated);
        Assert.False(actor.IsStaff);
    }

    private static EntraStaffApplicationActor CreateActor(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(
            claims,
            "test",
            "name",
            StaffAuthorization.RoleClaimType);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new EntraStaffApplicationActor(new HttpContextAccessor { HttpContext = context });
    }
}
