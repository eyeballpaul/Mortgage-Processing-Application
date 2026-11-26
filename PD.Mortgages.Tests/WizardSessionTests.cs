using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PD.Mortgages.Web.Security;
using PD.Mortgages.Web.Services;

namespace PD.Mortgages.Tests;

public sealed class WizardSessionTests
{
    [Fact]
    public void SessionActorIsStableAndDistinctBetweenSessions()
    {
        var first = CreateSession();
        var second = CreateSession();
        var actor = new SessionApplicationActor(first);

        Assert.Equal(first.ActorId, actor.SubjectId);
        Assert.Equal(actor.SubjectId, actor.SubjectId);
        Assert.NotEqual(first.ActorId, second.ActorId);
        Assert.False(actor.IsAuthenticated);
        Assert.False(actor.IsStaff);
    }

    [Fact]
    public void ClearingTheSessionRevokesItsDraftAndRotatesItsActor()
    {
        var session = CreateSession();
        var originalActor = session.ActorId;
        session.SetApplication(Guid.NewGuid(), 5);

        session.Clear();

        Assert.Null(session.CurrentApplicationId);
        Assert.Equal(1, session.CurrentStep);
        Assert.NotEqual(originalActor, session.ActorId);
    }

    private static WizardSession CreateSession()
    {
        var context = new DefaultHttpContext
        {
            Session = new TestSession(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "ignored"), new Claim(ClaimTypes.Role, "Staff")],
                "test"))
        };
        return new WizardSession(new TestHttpContextAccessor { HttpContext = context });
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
