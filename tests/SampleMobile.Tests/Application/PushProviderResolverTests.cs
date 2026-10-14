using NSubstitute;
using SampleMobile.Application.Abstractions.Push;
using SampleMobile.Application.Services;

namespace SampleMobile.Tests.Application;

public class PushProviderResolverTests {
    [Fact]
    public void A_registration_resolves_to_the_provider_with_its_exact_id() {
        var fcm = Provider("fcm");
        var simulated = Provider("simulated");
        var resolver = new PushProviderResolver([simulated, fcm]);

        Assert.True(resolver.TryResolve("FCM", out var resolved));
        Assert.Same(fcm, resolved);
    }

    [Fact]
    public void An_unknown_provider_id_never_falls_back_to_another_provider() {
        var resolver = new PushProviderResolver([Provider("simulated")]);

        Assert.False(resolver.TryResolve("fcm", out var resolved));
        Assert.Null(resolved);
        Assert.False(resolver.IsSupported("onesignal"));
    }

    [Fact]
    public void Two_adapters_cannot_claim_the_same_provider_id() {
        Assert.Throws<InvalidOperationException>(() => new PushProviderResolver([Provider("fcm"), Provider("FCM")]));
    }

    private static IPushNotificationProvider Provider(string id) {
        var provider = Substitute.For<IPushNotificationProvider>();
        provider.ProviderId.Returns(id);
        provider.Describe().Returns(new PushProviderDescriptor(id, id, false, true, null));
        return provider;
    }
}
