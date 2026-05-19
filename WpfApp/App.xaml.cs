using Autofac;
using ControlLibrary;
using Module.User.Services;
using Shared.Infrastructure.DependencyInjection;
using System;
using System.Reflection;
using System.Windows;

namespace WpfApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IContainer? _container;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _container = ServiceCollectionHelper.Build(WpfAppComposition.Register);
            ServiceCollectionHelper.Initialize(_container, WpfAppComposition.Initialize);
            AppLanguageManager.ApplyLanguage(AppLanguageManager.CurrentLanguage);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            LoginWindow loginWindow = _container.Resolve<LoginWindow>();
            bool? loginResult = loginWindow.ShowDialog();
            if (loginResult != true)
            {
                CurrentUserSession.SignOut();
                Shutdown();
                return;
            }

            MainWindow mainWindow = _container.Resolve<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _container?.Dispose();
            base.OnExit(e);
        }

        public static TEnum GetEnum<TEnum>(string text) where TEnum : struct
        {
            if (!typeof(TEnum).GetTypeInfo().IsEnum)
            {
                throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
            }
            return (TEnum)Enum.Parse(typeof(TEnum), text);
        }
    }
}
