using System;
using System.ComponentModel;

namespace WpfApp.Models.DataManagement;

/// <summary>
/// 测试数据展示模型；这里的 public 属性会自动进入数据绑定下拉框。
/// </summary>
public sealed class TestDataRecord
{
    // 备注：DisplayName 是配置界面下拉框里的中文显示名，属性名是真正的 BindingPath。
    [DisplayName("序号")]
    public int Index { get; set; }

    [DisplayName("条码")]
    public string Barcode { get; set; } = string.Empty;

    [DisplayName("工单号")]
    public string WorkOrder { get; set; } = string.Empty;

    [DisplayName("产品型号")]
    public string ProductModel { get; set; } = string.Empty;

    [DisplayName("工站")]
    public string StationName { get; set; } = string.Empty;

    [DisplayName("测试项")]
    public string TestItem { get; set; } = string.Empty;

    [DisplayName("测试值")]
    public double TestValue { get; set; }

    [DisplayName("上限")]
    public double UpperLimit { get; set; }

    [DisplayName("下限")]
    public double LowerLimit { get; set; }

    [DisplayName("结果")]
    public string Result { get; set; } = string.Empty;

    [DisplayName("操作员")]
    public string OperatorName { get; set; } = string.Empty;

    [DisplayName("开始时间")]
    public DateTime StartTime { get; set; }

    [DisplayName("结束时间")]
    public DateTime EndTime { get; set; }

    [DisplayName("耗时(ms)")]
    public int DurationMilliseconds { get; set; }

    [DisplayName("设备号")]
    public string EquipmentCode { get; set; } = string.Empty;

    [DisplayName("错误码")]
    public string ErrorCode { get; set; } = string.Empty;

    [DisplayName("备注")]
    public string Remarks { get; set; } = string.Empty;

    [DisplayName("哈哈哈")]
    public string hhh { get; set; } = string.Empty;
}
