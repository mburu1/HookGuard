using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using HookGuard.Application.Abstractions;
using HookGuard.Domain.Sources;
using NetArchTest.Rules;

namespace HookGuard.ArchitectureTests;

/// <summary>
/// The layering rules from ADR 0002, enforced instead of trusted. A violation fails the build.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(Source).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IReadinessProbe).Assembly;

    /// <summary>
    /// Assemblies and packages the inner layers must never reach for. Keeping this list explicit is
    /// the point: adding EF Core to Domain to "just save an entity" becomes a build error rather
    /// than a review comment.
    /// </summary>
    private static readonly string[] OuterLayers =
    [
        "HookGuard.Infrastructure",
        "HookGuard.Api",
        "HookGuard.Workers",
        "HookGuard.FakeMerchant",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.Logging",
        "Npgsql",
        "RabbitMQ",
        "StackExchange.Redis",
    ];

    [Fact]
    public void Domain_has_no_outward_dependencies()
    {
        AssertNoDependency(DomainAssembly, OuterLayers);
    }

    [Fact]
    public void Application_has_no_outward_dependencies()
    {
        AssertNoDependency(ApplicationAssembly, OuterLayers);
    }

    [Fact]
    public void Domain_types_live_under_the_domain_namespace()
    {
        AssertNamespace(DomainAssembly, "HookGuard.Domain");
    }

    [Fact]
    public void Application_types_live_under_the_application_namespace()
    {
        AssertNamespace(ApplicationAssembly, "HookGuard.Application");
    }

    [Fact]
    public void Signature_verification_does_not_reach_for_http_or_persistence()
    {
        // The verifier must stay a pure function of bytes, secret, timestamp and clock; a
        // HttpRequest-dependent verifier could only be tested through a web host.
        var result = Types.InNamespace("HookGuard.Domain.Signatures")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "System.Net.Http")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(Describe(result));
    }

    private static void AssertNoDependency(Assembly assembly, string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(Describe(result));
    }

    private static void AssertNamespace(Assembly assembly, string namespaceRoot)
    {
        // Reflected over directly rather than through NetArchTest: collection expressions and
        // iterators make the compiler emit nested helper types (such as
        // "<>z__ReadOnlySingleElementList`1" and its nested Enumerator) whose names are not valid
        // C# and which must not be policed. Only top-level types are checked; a nested type cannot
        // live in a namespace other than its parent's.
        var offenders = assembly.GetTypes()
            .Where(type => !type.IsNested && !IsCompilerGenerated(type))
            .Where(type => type.Namespace is not { } @namespace
                || !@namespace.StartsWith(namespaceRoot, StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        offenders.Should().BeNullOrEmpty(
            $"every hand-written type in {assembly.GetName().Name} must live under {namespaceRoot}");
    }

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.StartsWith('<')
        || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null
            ? "no types failed"
            : $"failing types: {string.Join(", ", result.FailingTypeNames)}";
}
