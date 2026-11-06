using System;
using System.Diagnostics;
using Autofac;
using ControlLibrary.Controls.MessageDialog;
using ControlLibrary.Controls.TestDataTable.Models;
using ControlLibrary.Models.EventsModels.Test;
using ControlLibrary.Models.MediatorModels.Business;
using Module.MES.Features.ApiConfig.ViewModels;
using Module.Business.Test.ViewModels;
using Module.User.Features.AccountManagement.Services;
using Module.User.Features.Authentication.Services;
using Shared.Infrastructure.DependencyInjection;
using Shared.Infrastructure.Events;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WpfApp;
using WpfApp.ViewModels;

namespace TestProject;

[TestFixture]
public sealed class DependencyInjectionRegressionTests
{
    [SetUp]
    public void SetUp()
    {
        EnsureWpfThreadContext();
        CurrentUserSession.SignOut();
    }

    [TearDown]
    public void TearDown()
    {
        CurrentUserSession.SignOut();
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Container_CanResolve_RuntimeViewsAndViewModels()
    {
        using IContainer container = BuildApplicationContainer();

        LoginWindow loginWindow = container.Resolve<LoginWindow>();
        MessageDialog messageDialog = container.Resolve<MessageDialog>();
        LoginWindowViewModel loginWindowViewModel = container.Resolve<LoginWindowViewModel>();
        ApiConfigViewModel apiConfigViewModel = container.Resolve<ApiConfigViewModel>();
        using TestViewModel testViewModel = container.Resolve<TestViewModel>();
        using TestMaxViewModel testMinViewModel = container.Resolve<TestMaxViewModel>();

        Assert.Multiple(() =>
        {
            Assert.That(loginWindow, Is.Not.Null);
            Assert.That(messageDialog, Is.Not.Null);
            Assert.That(loginWindowViewModel, Is.Not.Null);
            Assert.That(apiConfigViewModel, Is.Not.Null);
            Assert.That(testViewModel, Is.Not.Null);
            Assert.That(testMinViewModel, Is.Not.Null);
        });

        loginWindow.Close();
        messageDialog.Close();
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoginWindowViewModel_TryLogin_WithBuiltInAdminPassword_ReturnsTrue()
    {
        using IContainer container = BuildApplicationContainer();
        LoginWindowViewModel viewModel = container.Resolve<LoginWindowViewModel>();

        bool succeeded = viewModel.TryLogin(AccountConfigurationStore.BuiltInAdminPassword);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(CurrentUserSession.Current, Is.Not.Null);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoginWindowViewModel_TryLogin_WithInvalidPassword_ReturnsFalse()
    {
        using IContainer container = BuildApplicationContainer();
        LoginWindowViewModel viewModel = container.Resolve<LoginWindowViewModel>();

        bool succeeded = viewModel.TryLogin("invalid-password");

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(CurrentUserSession.Current, Is.Null);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TestViewModel_StationsShareInjectedEventAggregator()
    {
        using IContainer container = BuildApplicationContainer();
        IEventAggregator eventAggregator = container.Resolve<IEventAggregator>();
        using TestViewModel viewModel = container.Resolve<TestViewModel>();
        TestMaxViewModel firstStation = viewModel.Stations[0];

        viewModel.AddStationCommand.Execute(null);
        TestMaxViewModel secondStation = viewModel.Stations[1];

        eventAggregator
            .GetEvent<TestExecutionStatusChangedEvent>()
            .Publish(new TestExecutionStatusMessage(
                firstStation.StationName,
                "Running",
                "BARCODE-001",
                "Scheme-A",
                "Product-A"));

        eventAggregator
            .GetEvent<TestExecutionStatusChangedEvent>()
            .Publish(new TestExecutionStatusMessage(
                secondStation.StationName,
                "Completed",
                "BARCODE-002",
                "Scheme-B",
                "Product-B",
                isSuccess: true));

        DrainDispatcher();

        Assert.Multiple(() =>
        {
            Assert.That(firstStation.TestStatus, Is.EqualTo("Running"));
            Assert.That(firstStation.ProductBarcode, Is.EqualTo("BARCODE-001"));
            Assert.That(secondStation.TestStatus, Is.EqualTo("Completed"));
            Assert.That(secondStation.ProductBarcode, Is.EqualTo("BARCODE-002"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TestViewModel_StationGridColumns_UsesSingleColumnForSingleStation()
    {
        using IContainer container = BuildApplicationContainer();
        using TestViewModel viewModel = container.Resolve<TestViewModel>();

        Assert.That(viewModel.StationGridColumns, Is.EqualTo(1));

        viewModel.AddStationCommand.Execute(null);

        Assert.That(viewModel.StationGridColumns, Is.EqualTo(2));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TestMaxViewModel_SingleStep_ExecutesSelectedWorkStep()
    {
        using IContainer container = BuildApplicationContainer();
        using TestMaxViewModel viewModel = container.Resolve<TestMaxViewModel>();
        viewModel.SelectedProfile = TestProfileOption.FromScheme(new BusinessSchemeInfo(
            "test-scheme",
            "测试方案",
            [
                new BusinessSchemeWorkStepInfo("first-step", 1, "首个业务", "首个业务", 1),
                new BusinessSchemeWorkStepInfo("selected-step", 2, "测试业务", "测试业务", 1)
            ]));
        TestDataDisplayItem selectedStep = viewModel.WorkSteps.Last();

        viewModel.SelectedWorkStep = selectedStep;
        Assert.That(viewModel.SingleStepTestCommand.CanExecute(null), Is.True);
        viewModel.SingleStepTestCommand.Execute(null);
        Assert.That(viewModel.IsBusy, Is.True);
        Assert.That(viewModel.SingleStepTestCommand.CanExecute(null), Is.False);
        bool executionCompleted = WaitForDispatcherCondition(
            () => !viewModel.IsBusy,
            TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(executionCompleted, Is.True);
            Assert.That(viewModel.CurrentWorkStepName, Is.EqualTo(selectedStep.WorkStep));
            Assert.That(viewModel.SelectedWorkStep, Is.SameAs(selectedStep));
            Assert.That(viewModel.RunningLogs.First(), Does.Contain(selectedStep.WorkStep));
            Assert.That(viewModel.IsBusy, Is.False);
        });
    }

    private static IContainer BuildApplicationContainer()
    {
        return ServiceCollectionHelper.Build(builder =>
        {
            Type compositionType = typeof(LoginWindow).Assembly
                .GetType("WpfApp.WpfAppComposition", throwOnError: true)!;

            MethodInfo registerMethod = compositionType.GetMethod(
                "Register",
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("WpfAppComposition.Register was not found.");

            registerMethod.Invoke(null, [builder]);
        });
    }

    private static void EnsureWpfThreadContext()
    {
        _ = Application.Current ?? new Application();
        EnsureApplicationResources();

        if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        }
    }

    private static void DrainDispatcher()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);

        Dispatcher.PushFrame(frame);
    }

    private static bool WaitForDispatcherCondition(Func<bool> condition, TimeSpan timeout)
    {
        if (condition())
        {
            return true;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        DispatcherFrame frame = new();
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return condition();
    }

    private static void EnsureApplicationResources()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Any(dictionary => dictionary.Source?.OriginalString?.Contains(
                "AppDictionary.xaml",
                StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        string[] resourcePaths =
        [
            "Resources/Themes/DarkTheme.xaml",
            "Resources/AppDictionary.xaml",
            "Resources/TabControlStyles.xaml",
            "Resources/ComboBoxStyle.xaml",
            "Resources/TextBoxStyle.xaml",
            "Resources/ScrollBarStyle.xaml",
            "Resources/DataGridStyle.xaml",
            "Resources/ButtonStyle.xaml",
            "Resources/TitleStyle.xaml",
            "Resources/BorderStyle.xaml",
            "Resources/LabelStyle.xaml",
            "Resources/ListBoxStyle.xaml",
            "Resources/ContextMenuStyle.xaml",
            "Resources/ToolTipStyle.xaml",
            "Resources/Language/ZhCN.xaml"
        ];

        foreach (string resourcePath in resourcePaths)
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/WpfApp;component/{resourcePath}", UriKind.Relative)
            });
        }
    }
}
