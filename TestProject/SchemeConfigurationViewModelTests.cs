using Module.Business.Models;
using Module.Business.ViewModels;
using Module.Business.ViewModels.PropertyVMs;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using WpfApp;

namespace TestProject;

public class SchemeConfigurationViewModelTests
{
    [Test]
    public void HasModifiedOperationParameters_WhenOnlyReturnValueChanges_ReturnsTrue()
    {
        SchemeConfigurationViewModel viewModel = new();
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
        SchemeConfigurationViewModel viewModel = new();
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

    [Test]
    public void RefreshOperationParameterModifiedStates_WhenOperationHasSavedReturnSettings_RestoresModifiedFlag()
    {
        SchemeConfigurationViewModel viewModel = new();
        WorkStepOperation operation = new()
        {
            OperationObject = "System",
            InvokeMethod = "等待",
            ShowDataToView = true,
            ViewDataName = "Result"
        };

        Assert.That(operation.AreParametersModified, Is.False);

        viewModel.RefreshOperationParameterModifiedStates(new[] { operation });

        Assert.That(operation.AreParametersModified, Is.True);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EditingInvokeParameters_WhenTypeIsReturnValue_UsesPreviousStepKeysOnly()
    {
        _ = Application.Current ?? new App();

        Type editorStateType = typeof(SchemeConfigurationViewModel).Assembly
            .GetType("Module.Business.ViewModels.SchemeStepEditorState", throwOnError: true)!;
        object editorState = Activator.CreateInstance(editorStateType)!;

        WorkStepProfile workStep = new()
        {
            StepName = "工步 1"
        };

        WorkStepOperation previousOperation = new()
        {
            OperationObject = "System",
            InvokeMethod = "前序步骤",
            ReturnValue = "PrevKey"
        };

        WorkStepOperation currentOperation = new()
        {
            OperationObject = "System",
            InvokeMethod = "当前步骤",
            ReturnValue = "CurrentKey",
            Parameters = new ObservableCollection<WorkStepOperationParameter>
            {
                new()
                {
                    Name = "设置值",
                    ParameterName = "InputKey",
                    Value = string.Empty,
                    Remark = string.Empty
                }
            }
        };

        workStep.Steps.Add(previousOperation);
        workStep.Steps.Add(currentOperation);

        editorStateType.GetProperty("SelectedWorkStep")!.SetValue(editorState, workStep);
        editorStateType.GetMethod("OpenOperationDrawerForEdit")!.Invoke(editorState, new object[] { currentOperation });

        ObservableCollection<WorkStepOperationParameter> editingParameters =
            (ObservableCollection<WorkStepOperationParameter>)editorStateType
                .GetProperty("EditingInvokeParameters")!
                .GetValue(editorState)!;

        WorkStepOperationParameter parameter = editingParameters[0];
        parameter.Type = "返回值";

        Assert.That(parameter.ValueOptions, Does.Contain("PrevKey"));
        Assert.That(parameter.ValueOptions, Does.Not.Contain("CurrentKey"));
    }
}
