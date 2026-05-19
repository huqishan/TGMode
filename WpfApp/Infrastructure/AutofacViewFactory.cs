using Autofac;
using System;
using System.Windows;

namespace WpfApp.Infrastructure;

public sealed class AutofacViewFactory : IViewFactory
{
    private readonly ILifetimeScope _lifetimeScope;

    public AutofacViewFactory(ILifetimeScope lifetimeScope)
    {
        _lifetimeScope = lifetimeScope ?? throw new ArgumentNullException(nameof(lifetimeScope));
    }

    public FrameworkElement? Create(Type viewType)
    {
        if (viewType is null || !typeof(FrameworkElement).IsAssignableFrom(viewType))
        {
            return null;
        }

        return _lifetimeScope.TryResolve(viewType, out object? view)
            ? view as FrameworkElement
            : null;
    }
}
