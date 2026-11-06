using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using Module.Business.Features.BusinessConfiguration.ViewModels;
using Module.Business.Features.BusinessConfiguration.ViewModels.PresentationModels;
using System.Collections.ObjectModel;

namespace TestProject;

/// <summary>
/// 业务配置步骤排序回归测试。
/// </summary>
public sealed class BusinessConfigurationViewModelTests
{
    #region 序号排序

    [Test]
    public void MoveOperationToNumber_ShouldMoveStepAndRegenerateContinuousNumbers()
    {
        WorkStepOperation first = new() { Num = 1, Summary = "第一步" };
        WorkStepOperation second = new() { Num = 2, Summary = "第二步" };
        WorkStepOperation third = new() { Num = 3, Summary = "第三步" };
        BusinessProfile workStep = new()
        {
            Name = "排序测试业务",
            Operations = new ObservableCollection<WorkStepOperation> { first, second, third }
        };
        BusinessConfigurationViewModel viewModel = new();
        viewModel.WorkSteps.Clear();
        viewModel.WorkSteps.Add(workStep);
        viewModel.SelectedWorkStep = workStep;

        // 模拟用户把第三步的序号改为 1：该步骤应移动到首行，其余步骤依次后移。
        third.Num = 1;
        viewModel.MoveOperationToNumber(third);

        Assert.Multiple(() =>
        {
            Assert.That(workStep.Operations, Is.EqualTo(new[] { third, first, second }));
            Assert.That(workStep.Operations.Select(operation => operation.Num), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(viewModel.SelectedOperation, Is.SameAs(third));
        });
    }

    #endregion
}
