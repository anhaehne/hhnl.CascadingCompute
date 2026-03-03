using hhnl.CascadingCompute.Shared.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class GeneratedCascadingComputeDependencyInjectionTests
{
    [TestMethod]
    public void EnableCascadingCompute_should_replace_disabled_interface_registration_with_cascading_compute_wrapper()
    {
        // Arrange
        DisabledInterfaceService.Reset();
        var services = new ServiceCollection();
        services.AddTransient<IDisabledInterfaceService, DisabledInterfaceService>();

        // Act
        services.EnableCascadingCompute();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IDisabledInterfaceService>();
        _ = service.Compute(5);
        _ = service.Compute(5);

        // Assert
        Assert.IsInstanceOfType<DisabledInterfaceService.CascadingComputeWrapper>(service);
        Assert.AreEqual(1, DisabledInterfaceService.CallCount);
    }

    [TestMethod]
    public void EnableCascadingCompute_should_not_replace_enabled_interface_registration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<IEnabledInterfaceService, EnabledInterfaceService>();

        // Act
        services.EnableCascadingCompute();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IEnabledInterfaceService>();
        _ = service.Compute(5);
        _ = service.Compute(5);

        // Assert
        Assert.IsInstanceOfType<EnabledInterfaceService>(service);
        Assert.AreEqual(1, ((EnabledInterfaceService)service).CallCount);
    }

    [TestMethod]
    public void EnableCascadingCompute_should_replace_disabled_interface_registration_with_cascading_compute_wrapper_with_custom_init()
    {
        // Arrange
        DisabledInterfaceService.Reset();
        var services = new ServiceCollection();
        services.AddTransient<IDisabledInterfaceService>(sp => new DisabledInterfaceService());

        // Act
        services.EnableCascadingCompute();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IDisabledInterfaceService>();
        _ = service.Compute(5);
        _ = service.Compute(5);

        // Assert
        Assert.IsInstanceOfType<DisabledInterfaceService.CascadingComputeWrapper>(service);
        Assert.AreEqual(1, DisabledInterfaceService.CallCount);
    }

    public partial interface IDisabledInterfaceService
    {
        int Compute(int value);
    }

    public sealed partial class DisabledInterfaceService : IDisabledInterfaceService
    {
        public static int CallCount { get; private set; }

        public static void Reset()
            => CallCount = 0;

        [CascadingCompute]
        public int Compute(int value)
        {
            CallCount++;
            return value;
        }
    }

    public partial interface IEnabledInterfaceService
    {
        [CascadingCompute]
        int Compute(int value);
    }

    public sealed partial class EnabledInterfaceService : IEnabledInterfaceService
    {
        public int CallCount { get; set; }

        public int Compute(int value)
        {
            CallCount++;
            return value;
        }
    }
}
