using ControlLibrary;
using ControlLibrary.Models.MediatorModels.Business;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.Mediator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Module.Test.ViewModels;

public sealed class TestViewModel : ViewModelProperties, IDisposable
{
    private int _nextStationIndex = 1;
    private readonly IEventAggregator _eventAggregator;
    private readonly IMediator _mediator;
    private double _stationDisplayWidth;
    private double _stationItemWidth;
    private bool _disposed;

    public TestViewModel(IEventAggregator eventAggregator, IMediator mediator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

        Stations = new ObservableCollection<TestMaxViewModel>();
        AddStationCommand = new RelayCommand(_ => AddStation());
        RefreshStationsCommand = new AsyncRelayCommand(_ => LoadStationsAsync());
    }

    public ObservableCollection<TestMaxViewModel> Stations { get; }

    public ICommand AddStationCommand { get; }

    public ICommand RefreshStationsCommand { get; }

    public string StationCountText => $"共 {Stations.Count} 个工位";

    public double StationItemWidth
    {
        get => _stationItemWidth;
        private set => SetField(ref _stationItemWidth, value);
    }

    public void UpdateStationDisplayWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return;
        }

        _stationDisplayWidth = width;
        RefreshStationItemWidth();
    }

    public async Task LoadStationsAsync()
    {
        GetBusinessStationsResponse response = await _mediator.Send(new GetBusinessStationsRequest());
        BusinessStationInfo[] enabledStations = response.Stations
            .Where(station => station.IsEnabled)
            .ToArray();

        ReplaceStations(enabledStations);
    }

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

    private void ReplaceStations(IReadOnlyList<BusinessStationInfo> stations)
    {
        foreach (TestMaxViewModel station in Stations)
        {
            station.Dispose();
        }

        Stations.Clear();

        if (stations.Count == 0)
        {
            AddStation();
            return;
        }

        foreach (BusinessStationInfo station in stations)
        {
            Stations.Add(CreateStationViewModel(station.StationName));
        }

        int maxNumericSuffix = stations
            .Select(station => TryReadTrailingNumber(station.StationName, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        _nextStationIndex = Math.Max(maxNumericSuffix + 1, Stations.Count + 1);
        OnPropertyChanged(nameof(StationCountText));
        RefreshStationItemWidth();
    }

    private void AddStation()
    {
        Stations.Add(CreateStationViewModel($"工位 {_nextStationIndex++}"));
        OnPropertyChanged(nameof(StationCountText));
        RefreshStationItemWidth();
    }

    private TestMaxViewModel CreateStationViewModel(string stationName)
    {
        return new TestMaxViewModel(stationName, _eventAggregator, _mediator);
    }

    private static bool TryReadTrailingNumber(string value, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int end = value.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(value[end]))
        {
            end--;
        }

        int start = end;
        while (start >= 0 && char.IsDigit(value[start]))
        {
            start--;
        }

        return start < end &&
               int.TryParse(value.Substring(start + 1, end - start), out number);
    }

    private void RefreshStationItemWidth()
    {
        if (_stationDisplayWidth <= 0)
        {
            return;
        }

        int divisor = Math.Clamp(Stations.Count, 1, 3);
        StationItemWidth = _stationDisplayWidth / divisor;
    }
}
