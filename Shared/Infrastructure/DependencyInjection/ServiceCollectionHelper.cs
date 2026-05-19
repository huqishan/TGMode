using Autofac;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.Mediator;
using System;
using System.Reflection;

namespace Shared.Infrastructure.DependencyInjection;

public static class ServiceCollectionHelper
{
    public static IContainer Build(Action<ContainerBuilder>? configureApplicationServices = null)
    {
        return BuildContainer(configureApplicationServices);
    }

    public static void Initialize(
        ILifetimeScope scope,
        Action<ILifetimeScope>? initializeApplicationServices = null)
    {
        if (scope is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        initializeApplicationServices?.Invoke(scope);
    }
    public static void RegisterSharedServices(ContainerBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.RegisterType<EventAggregator>()
            .As<IEventAggregator>()
            .SingleInstance();

        builder.RegisterType<Shared.Infrastructure.Mediator.Mediator>()
            .As<IMediator>()
            .InstancePerLifetimeScope();
    }

    public static void RegisterMediatorHandlers(ContainerBuilder builder, params Assembly[] assemblies)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        builder.RegisterAssemblyTypes(assemblies)
            .AsClosedTypesOf(typeof(IRequestHandler<,>))
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterAssemblyTypes(assemblies)
            .AsClosedTypesOf(typeof(IRequestHandler<>))
            .AsImplementedInterfaces()
            .InstancePerDependency();
    }

    public static IContainer BuildContainer(Action<ContainerBuilder>? configure = null)
    {
        ContainerBuilder builder = new();
        RegisterSharedServices(builder);
        configure?.Invoke(builder);
        return builder.Build();
    }
}
