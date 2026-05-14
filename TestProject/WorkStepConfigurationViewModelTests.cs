using Module.Business.Models;
using Module.Business.ViewModels;

namespace TestProject;

public class WorkStepConfigurationViewModelTests
{
    [Test]
    public void HasModifiedOperationParameters_WhenOnlyReturnValueChanges_ReturnsTrue()
    {
        WorkStepConfigurationViewModel viewModel = new();
        WorkStepOperation operation = new()
        {
            OperationObject = "System",
            InvokeMethod = "等待",
            ReturnValue = "Result"
        };

        bool isModified = viewModel.HasModifiedOperationParameters(operation);

        Assert.That(isModified, Is.True);
    }

    [Test]
    public void HasModifiedOperationParameters_WhenOnlyReturnDisplaySettingsChange_ReturnsTrue()
    {
        WorkStepConfigurationViewModel viewModel = new();
        WorkStepOperation operation = new()
        {
            OperationObject = "System",
            InvokeMethod = "等待",
            ShowDataToView = true,
            ViewDataName = "Result"
        };

        bool isModified = viewModel.HasModifiedOperationParameters(operation);

        Assert.That(isModified, Is.True);
    }
}
