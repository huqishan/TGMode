using Module.Business.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Module.Business.ViewModels;

/// <summary>
/// 为可复用步骤编辑器提供单条步骤的独立编辑能力。
/// </summary>
public sealed partial class WorkStepConfigurationViewModel
{
    /// <summary>
    /// 以独立编辑模式打开单条步骤，不影响主工步配置页面的数据上下文。
    /// </summary>
    public void BeginStandaloneOperationEdit(
        WorkStepOperation? operation,
        string stepName = "流程图处理块",
        bool isDecisionOperationMode = false)
    {
        _isDecisionOperationMode = isDecisionOperationMode;
        WorkStepProfile temporaryWorkStep = new()
        {
            ProductName = stepName,
            StepName = string.IsNullOrWhiteSpace(stepName) ? "流程图处理块" : stepName.Trim(),
            Steps = new ObservableCollection<WorkStepOperation>()
        };

        WorkStepOperation editingOperation = operation?.Clone() ?? CreateDefaultStandaloneOperation();
        if (_isDecisionOperationMode &&
            (operation is null || string.IsNullOrWhiteSpace(operation.OperationObject)))
        {
            editingOperation.OperationObject = JudgeOperationObjectName;
        }

        WorkSteps.Clear();
        WorkSteps.Add(temporaryWorkStep);
        SelectedWorkStep = temporaryWorkStep;
        SelectedOperation = null;

        if (operation is not null)
        {
            temporaryWorkStep.Steps.Add(editingOperation);
            OpenOperationDrawerForEdit(editingOperation);
            return;
        }

        BeginOperationDrawer(editingOperation, isNewOperation: true);
        SetPageStatus(
            _isDecisionOperationMode ? "正在编辑流程图判断块。" : "正在编辑流程图处理块。",
            NeutralBrush);
    }

    /// <summary>
    /// 尝试保存当前独立编辑的步骤；若校验失败则保持编辑状态。
    /// </summary>
    public bool TrySaveStandaloneOperationEdit()
    {
        bool wasOpen = IsOperationDrawerOpen;
        SaveOperationDrawer();

        return wasOpen &&
               !IsOperationDrawerOpen &&
               SelectedWorkStep is not null &&
               SelectedWorkStep.Steps.Any();
    }

    /// <summary>
    /// 取消当前独立编辑。
    /// </summary>
    public void CancelStandaloneOperationEdit()
    {
        CloseOperationDrawer();
        SelectedOperation = null;
        SelectedWorkStep = null;
        WorkSteps.Clear();
        _isDecisionOperationMode = false;
    }

    /// <summary>
    /// 获取独立编辑后的当前步骤快照。
    /// </summary>
    public WorkStepOperation? CreateEditedOperationSnapshot()
    {
        WorkStepOperation? operation = SelectedWorkStep?.Steps.FirstOrDefault() ?? SelectedOperation;
        return operation?.Clone();
    }

    private static WorkStepOperation CreateDefaultStandaloneOperation()
    {
        return new WorkStepOperation
        {
            OperationObject = SystemOperationObjectName,
            InvokeMethod = string.Empty,
            ReturnValue = string.Empty,
            ShowDataToView = false,
            ViewDataName = string.Empty,
            ViewJudgeType = string.Empty,
            ViewJudgeCondition = string.Empty,
            DelayMilliseconds = 0,
            Remark = string.Empty
        };
    }
}
