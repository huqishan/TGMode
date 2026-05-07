using ControlLibrary;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Module.Test.ViewModels;

public sealed class TestViewModel : ViewModelProperties, IDisposable
{
    private int _nextStationIndex = 4;
    private bool _disposed;

    public TestViewModel()
    {
        Stations = new ObservableCollection<TestMinViewModel>
        {
            new("\u5de5\u4f4d 1"),
            new("\u5de5\u4f4d 2"),
            new("\u5de5\u4f4d 3")
        };

        AddStationCommand = new RelayCommand(_ => AddStation());
    }

    public ObservableCollection<TestMinViewModel> Stations { get; }

    public ICommand AddStationCommand { get; }

    public string StationCountText => $"\u5171 {Stations.Count} \u4e2a\u5de5\u4f4d";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (TestMinViewModel station in Stations)
        {
            station.Dispose();
        }

        _disposed = true;
    }

    private void AddStation()
    {
        Stations.Add(new TestMinViewModel($"\u5de5\u4f4d {_nextStationIndex++}"));
        OnPropertyChanged(nameof(StationCountText));
    }
}
