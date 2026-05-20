using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ControlLibrary.Controls.TestDataTable.Models;

public sealed class TestDataDisplayItem : INotifyPropertyChanged
{
    private string _workStep = string.Empty;
    private string _name = string.Empty;
    private string _testValue = string.Empty;
    private string _judgmentCondition = string.Empty;
    private string _result = string.Empty;
    private string _workStepElapsedTime = string.Empty;
    private bool _isCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WorkStep
    {
        get => _workStep;
        set => SetField(ref _workStep, value ?? string.Empty);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    public string TestValue
    {
        get => _testValue;
        set => SetField(ref _testValue, value ?? string.Empty);
    }

    public string JudgmentCondition
    {
        get => _judgmentCondition;
        set => SetField(ref _judgmentCondition, value ?? string.Empty);
    }

    public string Result
    {
        get => _result;
        set => SetField(ref _result, value ?? string.Empty);
    }

    public string WorkStepElapsedTime
    {
        get => _workStepElapsedTime;
        set => SetField(ref _workStepElapsedTime, value ?? string.Empty);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetField(ref _isCurrent, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
