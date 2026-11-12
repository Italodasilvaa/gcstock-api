using GCStock.Infra.Security;

namespace GCStock.IntegrationTests;

public class LoginAttemptTrackerTests
{
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (LoginAttemptTracker Tracker, ManualClock Clock) Create()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        return (new LoginAttemptTracker(clock), clock);
    }

    [Fact]
    public void Bloqueia_apos_o_limite_de_falhas()
    {
        var (tracker, _) = Create();

        for (var i = 0; i < LoginAttemptTracker.MaxFailures - 1; i++) tracker.RegisterFailure("dono@x.com");
        Assert.False(tracker.IsLocked("dono@x.com"));

        tracker.RegisterFailure("dono@x.com");
        Assert.True(tracker.IsLocked("dono@x.com"));
    }

    [Fact]
    public void Bloqueio_expira_e_a_contagem_recomeca()
    {
        var (tracker, clock) = Create();
        for (var i = 0; i < LoginAttemptTracker.MaxFailures; i++) tracker.RegisterFailure("dono@x.com");

        clock.Advance(LoginAttemptTracker.LockDuration + TimeSpan.FromSeconds(1));
        Assert.False(tracker.IsLocked("dono@x.com"));

        tracker.RegisterFailure("dono@x.com");
        Assert.False(tracker.IsLocked("dono@x.com"));
    }

    [Fact]
    public void Login_com_sucesso_zera_as_falhas()
    {
        var (tracker, _) = Create();
        for (var i = 0; i < LoginAttemptTracker.MaxFailures - 1; i++) tracker.RegisterFailure("dono@x.com");

        tracker.RegisterSuccess("dono@x.com");
        tracker.RegisterFailure("dono@x.com");

        Assert.False(tracker.IsLocked("dono@x.com"));
    }

    [Fact]
    public void Email_e_comparado_sem_diferenciar_maiusculas_nem_espacos()
    {
        var (tracker, _) = Create();
        for (var i = 0; i < LoginAttemptTracker.MaxFailures; i++) tracker.RegisterFailure("  Dono@X.com ");

        Assert.True(tracker.IsLocked("dono@x.com"));
        Assert.False(tracker.IsLocked("outro@x.com"));
    }
}
