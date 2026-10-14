using System.Reflection;
using SampleMobile.Mobile.Core;
using SampleMobile.Mobile.Core.Push;

namespace SampleMobile.Mobile.Tests.Architecture;

/// <summary>
/// The provider boundary on the client: models, API client, inbox and view models live in a library that cannot
/// see Firebase, Android or MAUI. A second push transport therefore can't require changes in any of them.
/// </summary>
public class MobileDependencyRuleTests {
    private static readonly string[] ForbiddenPrefixes = ["Xamarin.", "Firebase", "Google.", "Microsoft.Maui", "Mono.Android", "Java.Interop"];

    [Fact]
    public void The_core_library_references_no_firebase_android_or_maui_assembly() {
        var forbidden = typeof(DependencyInjection).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void No_public_core_type_names_a_specific_push_provider() {
        var named = typeof(DependencyInjection).Assembly.GetExportedTypes()
            .Where(type => type.Name.Contains("Firebase", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Fcm", StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(named);
    }

    [Fact]
    public void The_only_provider_id_the_core_knows_is_the_simulated_fallback() {
        var constants = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("SampleMobile.Mobile.Core", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .ToList();

        Assert.DoesNotContain("fcm", constants);
        Assert.Contains(InboxOnlyPushTransport.Id, constants);
    }
}
