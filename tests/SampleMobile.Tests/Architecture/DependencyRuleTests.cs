using System.Reflection;
using SampleMobile.Application;
using SampleMobile.Domain.Primitives;
using SampleMobile.Infrastructure.Persistence;

namespace SampleMobile.Tests.Architecture;

/// <summary>
/// The provider boundary, enforced. Project references already stop the inner layers from compiling
/// against vendor SDKs; these tests also catch a transitive reference sneaking in later.
/// </summary>
public class DependencyRuleTests {
    private static readonly string[] VendorPrefixes = ["FirebaseAdmin", "Google."];
    private static readonly string[] PersistenceAndWebPrefixes = ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore"];

    [Fact]
    public void The_domain_references_no_framework_or_vendor_assemblies() {
        var forbidden = ReferencedAssemblies(typeof(AggregateRoot).Assembly)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard" && name != "mscorlib")
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void The_application_layer_never_references_firebase_ef_core_or_aspnet_core() {
        var forbidden = ReferencedAssemblies(typeof(DependencyInjection).Assembly)
            .Where(name => VendorPrefixes.Concat(PersistenceAndWebPrefixes).Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Firebase_is_referenced_only_by_the_infrastructure_project() {
        Assert.Contains(ReferencedAssemblies(typeof(AppDbContext).Assembly), name => name == "FirebaseAdmin");
        Assert.DoesNotContain(ReferencedAssemblies(typeof(Program).Assembly), name => VendorPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static IReadOnlyList<string> ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToList();
}
