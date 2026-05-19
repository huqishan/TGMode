using ControlLibrary;
using Shared.Infrastructure.Events;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Module.Test.ViewModels;

public sealed class TestViewModel : ViewModelProperties, IDisposable
{
    private int _nextStationIndex = 4;
    private readonly IEventAggregator _eventAggregator;
    private bool _disposed;

    public TestViewModel(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        Stations = new ObservableCollection<TestMaxViewModel>
        {
            new("工位 1", _eventAggregator),
        };

        AddStationCommand = new RelayCommand(_ => AddStation());
    }

    public ObservableCollection<TestMaxViewModel> Stations { get; }

    public ICommand AddStationCommand { get; }

    public string StationCountText => $"共 {Stations.Count} 个工位";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (TestMaxViewModel station in Stations)
        {
            station.Dispose();
        }

        _disposed = true;
    }

    private void AddStation()
    {
        Stations.Add(new TestMaxViewModel($"工位 {_nextStationIndex++}", _eventAggregator));
        OnPropertyChanged(nameof(StationCountText));
    }
}
