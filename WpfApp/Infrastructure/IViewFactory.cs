using System;
using System.Windows;

namespace WpfApp.Infrastructure;

public interface IViewFactory
{
    FrameworkElement? Create(Type viewType);
}
