using ControlLibrary;
using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.MES.ViewModels;

public sealed class CommunicationConfigViewModel : ViewModelProperties
{
    private static readonly string MesConfigRootDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "MES_Config");

    private static readonly string MesSystemConfigFilePath =
        Path.Combine(MesConfigRootDirectory, "MesSystemConfig", "MesSystemConfig.json");

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    private string _timeoutText = "10";
    private string _retransmissionsText = "0";
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;

    public CommunicationConfigViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save());
        ReloadCommand = new RelayCommand(_ => Load());
        ResetCommand = new RelayCommand(_ => ResetToDefault());
        Load();
    }

    public string TimeoutText
    {
        get => _timeoutText;
        set
        {
            if (SetField(ref _timeoutText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(TimeoutSummary));
            }
        }
    }

    public string RetransmissionsText
    {
        get => _retransmissionsText;
        set
        {
            if (SetField(ref _retransmissionsText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(RetrySummary));
            }
        }
    }

    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    public string ConfigFilePath => MesSystemConfigFilePath;

    public string TimeoutSummary => int.TryParse(TimeoutText, out int seconds) && seconds > 0
        ? $"{seconds} 秒"
        : "请输入大于 0 的整数";

    public string RetrySummary => int.TryParse(RetransmissionsText, out int count) && count >= 0
        ? $"{count} 次"
        : "请输入不小于 0 的整数";

    public ICommand SaveCommand { get; }

    public ICommand ReloadCommand { get; }

    public ICommand ResetCommand { get; }

    private void Load()
    {
        MesSystemConfig config = ReadConfig();
        TimeoutText = config.TimeOut.ToString();
        RetransmissionsText = config.RetransmissionsNum.ToString();
        SetPageStatus(File.Exists(MesSystemConfigFilePath)
            ? "已读取通讯配置。"
            : "未发现通讯配置，已载入默认值。", NeutralBrush);
    }

    private void Save()
    {
        if (!TryBuildConfig(out MesSystemConfig config, out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        JsonHelper.SaveJson(config, MesSystemConfigFilePath);
        SetPageStatus("通讯配置已保存。", SuccessBrush);
    }

    private void ResetToDefault()
    {
        MesSystemConfig defaults = new();
        TimeoutText = defaults.TimeOut.ToString();
        RetransmissionsText = defaults.RetransmissionsNum.ToString();
        SetPageStatus("已恢复默认值，点击保存后生效。", NeutralBrush);
    }

    private bool TryBuildConfig(out MesSystemConfig config, out string message)
    {
        config = ReadConfig();

        if (!int.TryParse(TimeoutText?.Trim(), out int timeout) || timeout <= 0)
        {
            message = "接口超时时间必须是大于 0 的整数。";
            return false;
        }

        if (!int.TryParse(RetransmissionsText?.Trim(), out int retransmissions) || retransmissions < 0)
        {
            message = "重传次数必须是不小于 0 的整数。";
            return false;
        }

        config.TimeOut = timeout;
        config.RetransmissionsNum = retransmissions;
        message = string.Empty;
        return true;
    }

    private static MesSystemConfig ReadConfig()
    {
        try
        {
            return JsonHelper.ReadJson<MesSystemConfig>(MesSystemConfigFilePath) ?? new MesSystemConfig();
        }
        catch
        {
            return new MesSystemConfig();
        }
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }
}
