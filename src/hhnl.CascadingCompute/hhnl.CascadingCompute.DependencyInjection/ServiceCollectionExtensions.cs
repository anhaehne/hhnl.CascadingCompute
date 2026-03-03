using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Enables cascading compute for registered services by replacing them with cascading compute-enabled proxies where applicable.
    /// Call this method after registering all services and before building the service provider to ensure that cascading compute is properly enabled for all eligible services.
    /// </summary>
    public static IServiceCollection EnableCascadingCompute(this IServiceCollection services)
    {
        for (var index = 0; index < services.Count; index++)
        {
            var descriptor = services[index];
            if (!ShouldReplaceWithCascadingCompute(descriptor))
                continue;

            var implementationFactory = CreateImplementationFactory(descriptor);

            services[index] = ServiceDescriptor.Describe(
                descriptor.ServiceType,
                serviceProvider =>
                {
                    var implementation = implementationFactory(serviceProvider)
                        ?? throw new InvalidOperationException($"Service factory returned null for service type '{descriptor.ServiceType.FullName}'.");

                    var implementationType = implementation.GetType();
                    if (!HasCascadingComputeEnabledAttribute(implementationType))
                        return implementation;

                    var cascadingComputeProperty = implementationType.GetProperty("CascadingCompute", BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new InvalidOperationException($"Type '{implementationType.FullName}' is enabled for cascading compute but has no public CascadingCompute property.");

                    var cascadingCompute = cascadingComputeProperty.GetValue(implementation)
                        ?? throw new InvalidOperationException($"CascadingCompute property returned null for type '{implementationType.FullName}'.");

                    if (!descriptor.ServiceType.IsInstanceOfType(cascadingCompute))
                    {
                        throw new InvalidOperationException(
                            $"CascadingCompute value type '{cascadingCompute.GetType().FullName}' is not assignable to service type '{descriptor.ServiceType.FullName}'.");
                    }

                    return cascadingCompute;
                },
                descriptor.Lifetime);
        }

        return services;
    }

    private static bool ShouldReplaceWithCascadingCompute(ServiceDescriptor descriptor)
    {
        if (!descriptor.ServiceType.IsInterface)
            return false;

        if (HasCascadingComputeEnabledAttribute(descriptor.ServiceType))
            return false;

        if (descriptor.ImplementationType is not null)
            return HasCascadingComputeEnabledAttribute(descriptor.ImplementationType)
                   && descriptor.ServiceType.IsAssignableFrom(descriptor.ImplementationType);

        if (descriptor.ImplementationInstance is not null)
            return HasCascadingComputeEnabledAttribute(descriptor.ImplementationInstance.GetType())
                   && descriptor.ServiceType.IsAssignableFrom(descriptor.ImplementationInstance.GetType());

        return descriptor.ImplementationFactory is not null;
    }

    private static Func<IServiceProvider, object?> CreateImplementationFactory(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory;

        if (descriptor.ImplementationInstance is not null)
            return _ => descriptor.ImplementationInstance;

        if (descriptor.ImplementationType is not null)
            return serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);

        throw new InvalidOperationException($"Service descriptor for '{descriptor.ServiceType.FullName}' has no implementation.");
    }

    private static bool HasCascadingComputeEnabledAttribute(Type type)
        => type.GetCustomAttribute<hhnl.CascadingCompute.Attributes.CascadingComputeEnabledAttribute>(inherit: false) is not null;
}
