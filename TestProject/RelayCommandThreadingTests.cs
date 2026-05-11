using ControlLibrary;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TestProject;

public class RelayCommandThreadingTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void RaiseCanExecuteChanged_FromBackgroundThread_RaisesOnUiThread()
    {
        Application? application = Application.Current;
        bool createdApplication = false;
        if (application is null)
        {
            application = new Application();
            createdApplication = true;
        }

        try
        {
            RelayCommand command = new(_ => { });
            int uiThreadId = Environment.CurrentManagedThreadId;
            int eventThreadId = -1;
            using ManualResetEventSlim raised = new(false);

            command.CanExecuteChanged += (_, _) =>
            {
                eventThreadId = Environment.CurrentManagedThreadId;
                raised.Set();
            };

            Thread worker = new(command.RaiseCanExecuteChanged);
            worker.Start();
            worker.Join();

            PumpDispatcherUntil(() => raised.IsSet, TimeSpan.FromSeconds(2));

            Assert.That(raised.IsSet, Is.True, "CanExecuteChanged 未触发。");
            Assert.That(eventThreadId, Is.EqualTo(uiThreadId), "CanExecuteChanged 没有切回 UI 线程。");
        }
        finally
        {
            if (createdApplication)
            {
                application.Shutdown();
            }
        }
    }

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            DispatcherFrame frame = new();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
