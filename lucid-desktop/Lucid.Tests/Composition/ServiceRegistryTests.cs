using FluentAssertions;
using Lucid.Services.Infrastructure.Composition;
using Xunit;

namespace Lucid.Tests.Composition;

public sealed class ServiceRegistryTests
{
    private sealed class FakeService
    {
    }

    [Fact]
    public void Resolve_InvokesRegisteredFactory_PerCall()
    {
        var registry = new ServiceRegistry();
        var created = 0;
        registry.Register(() =>
        {
            created++;
            return new FakeService();
        });

        var first = registry.Resolve<FakeService>();
        var second = registry.Resolve<FakeService>();

        first.Should().NotBeSameAs(second, "registrations are transient factories");
        created.Should().Be(2);
    }

    [Fact]
    public void Resolve_WithoutRegistration_ThrowsWithTypeName()
    {
        var registry = new ServiceRegistry();

        var act = () => registry.Resolve<FakeService>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(FakeService)}*");
    }

    [Fact]
    public void Register_DuplicateType_Throws()
    {
        var registry = new ServiceRegistry();
        registry.Register(() => new FakeService());

        var act = () => registry.Register(() => new FakeService());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(FakeService)}*");
    }

    [Fact]
    public void Register_NullFactory_Throws()
    {
        var registry = new ServiceRegistry();

        var act = () => registry.Register<FakeService>(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
