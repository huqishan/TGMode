using Module.Business.Features.OperationEditing.Converters;
using Module.Business.Features.OperationEditing.ViewModels;
using Module.Business.Features.OperationEditing.ViewModels.PresentationModels;
using System.Collections.ObjectModel;

namespace TestProject;

/// <summary>
/// 步骤编辑器连续操作相关回归测试。
/// </summary>
[TestFixture]
public sealed class OperationEditorViewModelTests
{
    /// <summary>
    /// 验证连续新增时，第一次保存产生的返回值名称会立即进入后续参数下拉候选。
    /// </summary>
    [Test]
    public void Save_NewOperationTwice_RefreshesReturnValueParameterOptionsFromLatestHostCollection()
    {
        ObservableCollection<WorkStepOperation> operations = new();
        OperationEditorViewModel viewModel = new();
        viewModel.OperationSaved += (_, e) => operations.Add(e.Operation);

        WorkStepOperation operation = new()
        {
            OperationObjectName = "System",
            ReturnValue = "第一次",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterType = "返回值", ParameterName = "输入" }
            },
            ReturnValues = new ObservableCollection<ReturnValue>
            {
                new() { Num = 1, ReturnParameterName = "结果" }
            }
        };

        viewModel.Open(operation, isNewOperation: true, operations);
        viewModel.Save();

        Assert.That(viewModel.ParameterReturnValueOptions, Does.Contain("第一次_结果"));
    }

    /// <summary>
    /// 验证保存 Lua 步骤时根据返回表键生成并保存返回值定义。
    /// </summary>
    [Test]
    public void Save_LuaOperationReturningTables_SavesDistinctKeysFromAllReturns()
    {
        OperationEditorViewModel viewModel = new();
        WorkStepOperation? savedOperation = null;
        viewModel.OperationSaved += (_, e) => savedOperation = e.Operation;
        viewModel.Open(new WorkStepOperation
        {
            OperationObjectName = "Lua",
            PCommandName = "Lua",
            ReturnValue = "结果集合",
            LuaScript = "if Data == \"first\" then return { Status = Data, First = 1 } end return { Status = \"ok\", Second = 2 }",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterName = "Data", Value = "25" }
            }
        }, isNewOperation: true);

        viewModel.Save();

        Assert.That(savedOperation, Is.Not.Null);
        Assert.That(savedOperation!.ReturnValues.Select(item => item.ReturnParameterName).ToArray(),
            Is.EqualTo(new[] { "First", "Second", "Status" }));
        Assert.That(savedOperation.Parameters.Single().Value, Is.EqualTo("25"));
    }

    /// <summary>
    /// 验证保存 Lua 步骤时将标量返回值映射为 Data 返回键。
    /// </summary>
    [Test]
    public void Save_LuaOperationReturningScalar_SavesDataReturnKey()
    {
        OperationEditorViewModel viewModel = new();
        WorkStepOperation? savedOperation = null;
        viewModel.OperationSaved += (_, e) => savedOperation = e.Operation;
        viewModel.Open(new WorkStepOperation
        {
            OperationObjectName = "Lua",
            PCommandName = "Lua",
            ReturnValue = "计算结果",
            LuaScript = "return tonumber(Data) * 2",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterName = "Data", Value = "25" }
            }
        }, isNewOperation: true);

        viewModel.Save();

        Assert.That(savedOperation, Is.Not.Null);
        Assert.That(savedOperation!.ReturnValues.Select(item => item.ReturnParameterName).ToArray(),
            Is.EqualTo(new[] { "Data" }));
    }

    /// <summary>
    /// 验证没有返回值的 Lua 脚本无需填写返回值集合名也可以保存。
    /// </summary>
    [Test]
    public void Save_LuaOperationWithoutReturnValue_SavesEmptyReturnValues()
    {
        OperationEditorViewModel viewModel = new();
        WorkStepOperation? savedOperation = null;
        viewModel.OperationSaved += (_, e) => savedOperation = e.Operation;
        viewModel.Open(new WorkStepOperation
        {
            OperationObjectName = "Lua",
            PCommandName = "Lua",
            LuaScript = "local value = Data",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterName = "Data", Value = "25" }
            }
        }, isNewOperation: true);

        viewModel.Save();

        Assert.That(savedOperation, Is.Not.Null);
        Assert.That(savedOperation!.ReturnValues, Is.Empty);
    }

    /// <summary>
    /// 验证连续新增过程中重建参数集合时，新出现的返回值参数行会装载已有步骤的最新返回值名称。
    /// </summary>
    [Test]
    public void Open_NewOperationAfterPreviousSave_RebuiltReturnValueRowUsesLatestOptions()
    {
        ObservableCollection<WorkStepOperation> operations = new()
        {
            new()
            {
                ReturnValue = "上一步",
                ReturnValues = new ObservableCollection<ReturnValue>
                {
                    new() { Num = 1, ReturnParameterName = "结果" }
                }
            }
        };
        OperationEditorViewModel viewModel = new();
        WorkStepOperation operation = new()
        {
            OperationObjectName = "System",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterType = "返回值", ParameterName = "输入" }
            }
        };

        viewModel.Open(operation, isNewOperation: true, operations);

        Assert.That(viewModel.ParameterReturnValueOptions, Does.Contain("上一步_结果"));
    }

    /// <summary>
    /// 验证修改中间步骤后，返回值参数候选只包含该步骤之前的数据。
    /// </summary>
    [Test]
    public void Save_ExistingOperation_ReturnValueOptionsContainOnlyPreviousOperations()
    {
        WorkStepOperation first = new()
        {
            ReturnValue = "第一步",
            ReturnValues = new ObservableCollection<ReturnValue> { new() { ReturnParameterName = "结果" } }
        };
        WorkStepOperation current = new()
        {
            OperationObjectName = "System",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "返回值", ParameterName = "输入" }
            }
        };
        WorkStepOperation later = new()
        {
            ReturnValue = "后续步骤",
            ReturnValues = new ObservableCollection<ReturnValue> { new() { ReturnParameterName = "结果" } }
        };
        ObservableCollection<WorkStepOperation> operations = new() { first, current, later };
        OperationEditorViewModel viewModel = new();
        viewModel.OperationSaved += (_, e) => operations[1] = e.Operation;

        viewModel.Open(current, isNewOperation: false, operations);
        viewModel.Save();

        IEnumerable<string> options = viewModel.ParameterReturnValueOptions;
        Assert.Multiple(() =>
        {
            Assert.That(options, Does.Contain("第一步_结果"));
            Assert.That(options, Does.Not.Contain("后续步骤_结果"));
        });
    }

    /// <summary>
    /// 验证业务值参数显示当前业务所有步骤中业务值类型的参数值。
    /// </summary>
    [Test]
    public void Open_OperationWithWorkStepValueParameter_ShowsPreviousWorkStepValuesOnly()
    {
        WorkStepOperation first = new()
        {
            ReturnValue = "不应显示的返回值集合名",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "业务值", ParameterName = "不应显示的参数名", Value = "前置业务参数值" },
                new() { ParameterType = "设置值", ParameterName = "普通参数名", Value = "普通参数值" }
            }
        };
        WorkStepOperation current = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "业务值", ParameterName = "输入" }
            }
        };
        WorkStepOperation later = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "业务值", ParameterName = "后续参数名", Value = "后续业务参数值" }
            }
        };
        ObservableCollection<WorkStepOperation> operations = new() { first, current, later };
        OperationEditorViewModel viewModel = new();

        viewModel.Open(current, isNewOperation: false, operations);

        IEnumerable<string> options = viewModel.WorkStepValueOptions;
        Assert.Multiple(() =>
        {
            Assert.That(options, Does.Contain("前置业务参数值"));
            Assert.That(options, Does.Not.Contain("不应显示的返回值集合名"));
            Assert.That(options, Does.Not.Contain("不应显示的参数名"));
            Assert.That(options, Does.Not.Contain("普通参数值"));
            Assert.That(options, Does.Contain("后续业务参数值"));
        });
    }

    /// <summary>
    /// 验证连续新增时，上一步骤中业务值类型输入的参数值会立即进入下拉候选。
    /// </summary>
    [Test]
    public void Save_NewOperation_RefreshesWorkStepValueOptionsFromSavedInputParameterValues()
    {
        ObservableCollection<WorkStepOperation> operations = new();
        OperationEditorViewModel viewModel = new();
        viewModel.OperationSaved += (_, e) => operations.Add(e.Operation);
        WorkStepOperation operation = new()
        {
            OperationObjectName = "System",
            Parameters = new ObservableCollection<InputParameter>
            {
                new()
                {
                    ParameterType = "业务值",
                    ParameterName = "测试参数",
                    Value = "用户输入的业务值"
                }
            }
        };

        viewModel.Open(operation, isNewOperation: true, operations);
        viewModel.Save();

        Assert.That(viewModel.WorkStepValueOptions, Does.Contain("用户输入的业务值"));
    }

    /// <summary>
    /// 验证业务值候选按当前业务内所有步骤及其输入参数的顺序显示，不按名称排序。
    /// </summary>
    [Test]
    public void Open_WorkStepValueOptions_PreservePreviousStepAndParameterOrder()
    {
        WorkStepOperation first = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterType = "业务值", Value = "Z-第一步第一个" },
                new() { Num = 2, ParameterType = "业务值", Value = "A-第一步第二个" }
            }
        };
        WorkStepOperation second = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { Num = 1, ParameterType = "业务值", Value = "M-第二步" },
                new() { Num = 2, ParameterType = "业务值", Value = "Z-第一步第一个" }
            }
        };
        WorkStepOperation current = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "业务值" }
            }
        };
        OperationEditorViewModel viewModel = new();

        viewModel.Open(current, isNewOperation: false, new[] { first, second, current });

        Assert.That(viewModel.WorkStepValueOptions,
            Is.EqualTo(new[] { "Z-第一步第一个", "A-第一步第二个", "M-第二步" }));
    }

    /// <summary>
    /// 验证返回值候选按前面步骤及其返回值键的顺序显示，不按名称排序。
    /// </summary>
    [Test]
    public void Open_ReturnValueOptions_PreservePreviousStepAndReturnValueOrder()
    {
        WorkStepOperation first = new()
        {
            ReturnValue = "Z-第一步",
            ReturnValues = new ObservableCollection<ReturnValue>
            {
                new() { Num = 1, ReturnParameterName = "第一个" },
                new() { Num = 2, ReturnParameterName = "第二个" }
            }
        };
        WorkStepOperation second = new()
        {
            ReturnValue = "A-第二步",
            ReturnValues = new ObservableCollection<ReturnValue>
            {
                new() { Num = 1, ReturnParameterName = "结果" }
            }
        };
        WorkStepOperation current = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "返回值" }
            }
        };
        OperationEditorViewModel viewModel = new();

        viewModel.Open(current, isNewOperation: false, new[] { first, second, current });

        Assert.That(viewModel.ParameterReturnValueOptions,
            Is.EqualTo(new[] { "Z-第一步_第一个", "Z-第一步_第二个", "A-第二步_结果" }));
    }

    /// <summary>
    /// 验证条件执行左右参数按类型装载与输入参数相同的返回值和业务值候选。
    /// </summary>
    [Test]
    public void Open_ConditionParameterTypes_LoadMatchingValueOptions()
    {
        WorkStepOperation previous = new()
        {
            ReturnValue = "前步骤",
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "业务值", Value = "业务参数值" }
            },
            ReturnValues = new ObservableCollection<ReturnValue>
            {
                new() { ReturnParameterName = "结果" }
            }
        };
        WorkStepOperation current = new()
        {
            ConditionExecution = new ConditionExecution
            {
                LeftParameterType = "返回值",
                RightParameterType = "业务值"
            }
        };
        OperationEditorViewModel viewModel = new();

        viewModel.Open(current, isNewOperation: false, new[] { previous, current });

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ParameterReturnValueOptions, Is.EqualTo(new[] { "前步骤_结果" }));
            Assert.That(viewModel.WorkStepValueOptions, Is.EqualTo(new[] { "业务参数值" }));
        });
    }

    /// <summary>
    /// 验证保存后刷新共享候选集合时，条件执行左右参数值保持不变。
    /// </summary>
    [Test]
    public void Save_ConditionExecution_PreservesLeftAndRightValuesAfterOptionRefresh()
    {
        ObservableCollection<WorkStepOperation> operations = new();
        OperationEditorViewModel viewModel = new();
        viewModel.OperationSaved += (_, e) => operations.Add(e.Operation);
        WorkStepOperation operation = new()
        {
            OperationObjectName = "System",
            ConditionExecution = new ConditionExecution
            {
                IsEnabled = true,
                LeftParameterType = "业务值",
                LeftValue = "左参数值",
                RightParameterType = "设置值",
                RightValue = "右参数值"
            }
        };

        viewModel.Open(operation, isNewOperation: true, operations);
        viewModel.Save();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EditingOperation.ConditionExecution.LeftValue, Is.EqualTo("左参数值"));
            Assert.That(viewModel.EditingOperation.ConditionExecution.RightValue, Is.EqualTo("右参数值"));
            Assert.That(operations.Single().ConditionExecution.LeftValue, Is.EqualTo("左参数值"));
            Assert.That(operations.Single().ConditionExecution.RightValue, Is.EqualTo("右参数值"));
        });
    }

    /// <summary>
    /// 验证当前编辑步骤新增业务值后，无需保存即可刷新共享候选集合。
    /// </summary>
    [Test]
    public void RefreshWorkStepValueOptions_UsesUnsavedEditingParameterValue()
    {
        WorkStepOperation operation = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "设置值", Value = "原参数值" }
            }
        };
        ObservableCollection<WorkStepOperation> operations = new() { operation };
        OperationEditorViewModel viewModel = new();
        viewModel.Open(operation, isNewOperation: false, operations);
        viewModel.EditingOperation.Parameters[0].ParameterType = "业务值";
        viewModel.EditingOperation.Parameters[0].Value = "未保存的新业务值";

        viewModel.RefreshWorkStepValueOptionsFromEditingOperation();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.WorkStepValueOptions, Does.Contain("未保存的新业务值"));
            Assert.That(viewModel.WorkStepValueOptions, Does.Not.Contain("原参数值"));
        });
    }

    /// <summary>
    /// 验证条件执行左右参数新增的业务值名称无需保存即可进入共享候选集合。
    /// </summary>
    [Test]
    public void RefreshWorkStepValueOptions_IncludesUnsavedConditionValues()
    {
        WorkStepOperation operation = new()
        {
            Parameters = new ObservableCollection<InputParameter>
            {
                new() { ParameterType = "业务值", Value = "输入参数业务值" }
            },
            ConditionExecution = new ConditionExecution
            {
                LeftParameterType = "业务值",
                LeftValue = "左参数新业务值",
                RightParameterType = "业务值",
                RightValue = "右参数新业务值"
            }
        };
        OperationEditorViewModel viewModel = new();
        viewModel.Open(operation, isNewOperation: true, Array.Empty<WorkStepOperation>());

        viewModel.RefreshWorkStepValueOptionsFromEditingOperation();

        Assert.That(viewModel.WorkStepValueOptions, Is.EqualTo(new[]
        {
            "输入参数业务值",
            "左参数新业务值",
            "右参数新业务值"
        }));
    }
}


