using SampleMobile.Domain.Entities;

namespace SampleMobile.Tests.Domain;

public class DeviceRegistrationTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Provider_ids_are_normalized() {
        var registration = DeviceRegistration.Register(Guid.CreateVersion7(), "install", DevicePlatform.Android, " FCM ", "token", Now);

        Assert.Equal("fcm", registration.ProviderId);
    }

    [Fact]
    public void Refreshing_with_a_rotated_token_updates_the_address() {
        var owner = Guid.CreateVersion7();
        var registration = DeviceRegistration.Register(owner, "install", DevicePlatform.Android, "fcm", "token-1", Now);

        var transferred = registration.Refresh(owner, DevicePlatform.Android, "token-2", Now.AddDays(1));

        Assert.False(transferred);
        Assert.Equal("token-2", registration.Address);
        Assert.Equal(Now.AddDays(1), registration.UpdatedAt);
    }

    [Fact]
    public void Refreshing_as_another_user_moves_the_installation_to_that_user() {
        var registration = DeviceRegistration.Register(Guid.CreateVersion7(), "install", DevicePlatform.Android, "fcm", "token", Now);
        var newOwner = Guid.CreateVersion7();

        var transferred = registration.Refresh(newOwner, DevicePlatform.Android, "token", Now.AddHours(1));

        Assert.True(transferred);
        Assert.Equal(newOwner, registration.UserId);
    }

    [Fact]
    public void Refreshing_a_disabled_registration_re_enables_it() {
        var owner = Guid.CreateVersion7();
        var registration = DeviceRegistration.Register(owner, "install", DevicePlatform.Android, "fcm", "token", Now);
        registration.Disable(DeviceDisabledReason.InvalidatedByProvider, Now.AddHours(1));

        registration.Refresh(owner, DevicePlatform.Android, "fresh-token", Now.AddHours(2));

        Assert.True(registration.IsEnabled);
        Assert.Null(registration.DisabledReason);
    }

    [Fact]
    public void Disabling_twice_keeps_the_first_reason() {
        var registration = DeviceRegistration.Register(Guid.CreateVersion7(), "install", DevicePlatform.Android, "fcm", "token", Now);

        registration.Disable(DeviceDisabledReason.InvalidatedByProvider, Now);
        registration.Disable(DeviceDisabledReason.Unregistered, Now.AddMinutes(1));

        Assert.Equal(DeviceDisabledReason.InvalidatedByProvider, registration.DisabledReason);
    }
}
