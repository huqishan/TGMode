using Autofac;
using ControlLibrary;
using Module.Business.Services;
using Module.Communication.Services;
using Module.MES.Services;
using Module.Test.Services;
using Module.User.Services;
using Shared.Infrastructure.DependencyInjection;
using Shared.Infrastructure.Events;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WpfApp.Infrastructure;
using BusinessSystem = Module.Business.Business.System;

namespace WpfApp;

internal static class WpfAppComposition
{
    public static void Register(ContainerBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.RegisterType<AutofacViewFactory>()
            .As<IViewFactory>()
            .SingleInstance();

        RegisterApplicationServices(builder);
        RegisterViewsAndViewModels(builder);
    }

    public static void Initialize(ILifetimeScope scope)
    {
        IEventAggregator eventAggregator = scope.Resolve<IEventAggregator>();
        BusinessSystem.ConfigureEventAggregator(eventAggregator);

        // Resolve long-lived module services that subscribe to application events.
        _ = scope.Resolve<BusinessService>();
    }

    private static void RegisterApplicationServices(ContainerBuilder builder)
    {
        builder.RegisterType<BusinessService>().SingleInstance();
        builder.RegisterType<CommunicationService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<MESService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<TestService>().SingleInstance();
        builder.RegisterType<AuthenticationService>()
            .As<IAuthenticationService>()
            .SingleInstance();
        builder.RegisterType<UserService>().SingleInstance();
    }

    private static void RegisterViewsAndViewModels(ContainerBuilder builder)
    {
        Assembly[] assemblies =
        [
            typeof(App).Assembly,
            typeof(Module.Business.Views.SchemeConfigurationView).Assembly,
            typeof(Module.Communication.Views.DeviceCommunicationConfigView).Assembly,
            typeof(Module.MES.Views.ApiConfigView).Assembly,
            typeof(Module.Test.Views.TestView).Assembly,
            typeof(Module.User.Views.AccountManagementView).Assembly,
            typeof(ControlLibrary.Controls.Navigation.Control.ModernNavigationBar).Assembly
        ];

        ServiceCollectionHelper.RegisterMediatorHandlers(builder, assemblies);

        builder.RegisterAssemblyTypes(assemblies)
            .Where(type => typeof(Window).IsAssignableFrom(type) || typeof(UserControl).IsAssignableFrom(type))
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterAssemblyTypes(assemblies)
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .AsSelf()
            .InstancePerDependency();
    }
}
