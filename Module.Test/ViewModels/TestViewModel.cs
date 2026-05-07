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
            new("工位 1"),
            new("工位 2"),
            new("工位 3")
        };

        AddStationCommand = new RelayCommand(_ => AddStation());
    }

    public ObservableCollection<TestMinViewModel> Stations { get; }

    public ICommand AddStationCommand { get; }

    public string StationCountText => $"共 {Stations.Count} 个工位";

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
        Stations.Add(new TestMinViewModel($"工位 {_nextStationIndex++}"));
        OnPropertyChanged(nameof(StationCountText));
    }
}
