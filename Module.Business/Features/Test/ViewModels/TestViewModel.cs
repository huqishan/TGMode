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

namespace Module.Business.Test.ViewModels;

/// <summary>
/// 通信测试主页 ViewModel。
/// 管理工位测试卡片集合：支持手动新增工位、从工位配置加载启用工位，
/// 并根据可视区域高度自动计算卡片的显示高度。
/// </summary>
public sealed class TestViewModel : ViewModelProperties, IDisposable
{
    #region 字段

    /// <summary>下一个新增工位的编号，命名会自动避开已存在的数字后缀。</summary>
    private int _nextStationIndex = 1;

    /// <summary>事件聚合器，传递给工位卡片 ViewModel 用于订阅通信事件。</summary>
    private readonly IEventAggregator _eventAggregator;

    /// <summary>中介者，用于向工位配置发起查询请求。</summary>
    private readonly IMediator _mediator;

    /// <summary>工位卡片可视区域高度，由界面根据 ScrollViewer 尺寸回传。</summary>
    private double _stationDisplayHeight;

    /// <summary>单个工位卡片的显示高度，随工位数量在整屏与半屏之间切换。</summary>
    private double _stationItemHeight;

    /// <summary>是否已释放，保证 Dispose 幂等。</summary>
    private bool _disposed;

    #endregion

    #region 构造函数

    /// <summary>
    /// 构造函数，初始化命令并默认创建一个空白工位卡片。
    /// </summary>
    /// <param name="eventAggregator">事件聚合器。</param>
    /// <param name="mediator">中介者。</param>
    public TestViewModel(IEventAggregator eventAggregator, IMediator mediator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

        Stations = new ObservableCollection<TestMaxViewModel>();
        AddStationCommand = new RelayCommand(_ => AddStation());
        RefreshStationsCommand = new AsyncRelayCommand(_ => LoadStationsAsync());
        AddStation();
    }

    #endregion

    #region 属性与命令

    /// <summary>工位测试卡片集合。</summary>
    public ObservableCollection<TestMaxViewModel> Stations { get; }

    /// <summary>新增工位命令。</summary>
    public ICommand AddStationCommand { get; }

    /// <summary>从工位配置刷新启用工位命令。</summary>
    public ICommand RefreshStationsCommand { get; }

    /// <summary>界面展示的工位数量文案。</summary>
    public string StationCountText => $"共 {Stations.Count} 个工位";

    /// <summary>卡片网格列数，单个工位占满整行，多个工位按两列布局。</summary>
    public int StationGridColumns => Stations.Count <= 1 ? 1 : 2;

    /// <summary>单个工位卡片的显示高度。</summary>
    public double StationItemHeight
    {
        get => _stationItemHeight;
        private set => SetField(ref _stationItemHeight, value);
    }

    #endregion

    #region 公开方法

    /// <summary>
    /// 界面回传可视区域高度，用于重新计算卡片高度。
    /// </summary>
    /// <param name="height">可视区域高度，非法值直接忽略。</param>
    public void UpdateStationDisplayHeight(double height)
    {
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
        {
            return;
        }

        _stationDisplayHeight = height;
        RefreshStationItemHeight();
    }

    /// <summary>
    /// 从工位配置加载已启用的工位并整体替换卡片集合。
    /// </summary>
    public async Task LoadStationsAsync()
    {
        GetBusinessStationsResponse response = await _mediator.Send(new GetBusinessStationsRequest());
        BusinessStationInfo[] enabledStations = response.Stations
            .Where(station => station.IsEnabled)
            .ToArray();

        ReplaceStations(enabledStations);
    }

    /// <summary>
    /// 释放全部工位卡片 ViewModel，界面卸载时调用。
    /// </summary>
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

    #endregion

    #region 内部方法

    /// <summary>
    /// 用工位信息整体替换卡片集合，并依据已有名称的数字后缀续接新编号。
    /// 无启用工位时回退为创建一个空白卡片，保证界面始终有可操作区域。
    /// </summary>
    /// <param name="stations">启用的工位信息集合。</param>
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

        // 取所有名称尾部数字的最大值作为新编号起点，避免新增工位与已有工位重名。
        int maxNumericSuffix = stations
            .Select(station => TryReadTrailingNumber(station.StationName, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        _nextStationIndex = Math.Max(maxNumericSuffix + 1, Stations.Count + 1);
        OnPropertyChanged(nameof(StationCountText));
        OnPropertyChanged(nameof(StationGridColumns));
        RefreshStationItemHeight();
    }

    /// <summary>新增一个空白工位卡片并刷新数量与高度。</summary>
    private void AddStation()
    {
        Stations.Add(CreateStationViewModel($"工位 {_nextStationIndex++}"));
        OnPropertyChanged(nameof(StationCountText));
        OnPropertyChanged(nameof(StationGridColumns));
        RefreshStationItemHeight();
    }

    /// <summary>创建工位卡片 ViewModel。</summary>
    /// <param name="stationName">工位名称。</param>
    private TestMaxViewModel CreateStationViewModel(string stationName)
    {
        return new TestMaxViewModel(stationName, _eventAggregator, _mediator);
    }

    /// <summary>
    /// 读取名称尾部的数字后缀，如“工位 3”返回 3。
    /// </summary>
    /// <param name="value">工位名称。</param>
    /// <param name="number">解析出的数字。</param>
    /// <returns>尾部存在数字时返回 true。</returns>
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

    /// <summary>按工位数量重新计算卡片高度：超过两个时每张卡片占一半高度。</summary>
    private void RefreshStationItemHeight()
    {
        if (_stationDisplayHeight <= 0)
        {
            return;
        }

        StationItemHeight = Stations.Count > 2
            ? _stationDisplayHeight / 2.0
            : _stationDisplayHeight;
    }

    #endregion
}
