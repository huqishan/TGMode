using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using Module.Business.Models;
using Module.Business.Services;
using Module.Business.ViewModels;

namespace TestProject;

[Apartment(ApartmentState.STA)]
public class BusinessConfigurationViewModelTests
{
    [SetUp]
    public void SetUp()
    {
        BusinessConfigurationStore.SaveCatalog(new BusinessConfigurationCatalog());
    }

    [TearDown]
    public void TearDown()
    {
        BusinessConfigurationStore.SaveCatalog(new BusinessConfigurationCatalog());
    }

    [Test]
    public void WorkStepRefreshReloadsSavedWorkSteps()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("step-1", "产品A", "工步1", "设备", "启动")));

        WorkStepConfigurationViewModel viewModel = new();
        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("工步1"));

        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("step-1", "产品A", "工步1", "设备", "启动"),
            CreateWorkStep("step-2", "产品A", "导入工步", "设备", "停止")));

        viewModel.RefreshProductsCommand.Execute(null);

        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("导入工步"));
    }

    [Test]
    public void ImportedWorkStepUsesSchemeStepNameAndSchemeName()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(new ProductProfile { ProductName = "产品A" }));
        SchemeConfigurationViewModel viewModel = new();
        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "source-step",
                        ProductName = "产品A",
                        StepName = "原工步",
                        OperationSummary = "设备.启动"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("source-step", "产品A", string.Empty, "设备", "启动")
            }
        };

        InvokeImportSchemePackage(viewModel, package);

        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("原工步_方案A"));
    }

    [Test]
    public void ImportSchemeReloadsLocalProductsAndWorkStepsBeforeReuseDecision()
    {
        SchemeConfigurationViewModel viewModel = new();
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("local-step", "产品A", "工步1", "设备", "启动")));

        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "import-step",
                        ProductName = "产品A",
                        StepName = "工步1",
                        OperationSummary = "设备.启动"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("import-step", "产品A", "工步1", "设备", "启动")
            }
        };

        InvokeImportSchemePackage(viewModel, package);

        Assert.That(viewModel.ProductOptions, Does.Contain("产品A"));
        Assert.That(viewModel.WorkSteps.Select(step => step.Id), Does.Contain("local-step"));
        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Not.Contain("工步1_方案A"));
        Assert.That(viewModel.Schemes.Single().ProductName, Is.EqualTo("产品A"));
        Assert.That(viewModel.Schemes.Single().Steps.Single().WorkStepId, Is.EqualTo("local-step"));
    }

    [Test]
    public void ImportedWorkStepWithDifferentNameCreatesNewWorkStep()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("existing-step", "产品A", "现有工步", "设备", "启动")));

        SchemeConfigurationViewModel viewModel = new();
        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "source-step",
                        ProductName = "产品A",
                        StepName = "导入工步",
                        OperationSummary = "设备.启动"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("source-step", "产品A", "导入工步", "设备", "启动")
            }
        };

        InvokeImportSchemePackage(viewModel, package);

        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("现有工步"));
        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("导入工步_方案A"));
    }

    [Test]
    public void ImportedWorkStepWithDifferentOperationsCreatesNewWorkStep()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("existing-step", "产品A", "工步1", "设备", "启动")));

        SchemeConfigurationViewModel viewModel = new();
        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "source-step",
                        ProductName = "产品A",
                        StepName = "工步1",
                        OperationSummary = "设备.停止"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("source-step", "产品A", "工步1", "设备", "停止")
            }
        };

        InvokeImportSchemePackage(viewModel, package);

        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("工步1"));
        Assert.That(viewModel.WorkSteps.Select(step => step.StepName), Does.Contain("工步1_方案A"));
    }

    [Test]
    public void ImportedWorkStepWithSameIdAndDifferentOperationsDoesNotOverwriteExistingWorkStep()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("same-step", "产品A", "工步1", "设备", "启动")));

        SchemeConfigurationViewModel viewModel = new();
        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "same-step",
                        ProductName = "产品A",
                        StepName = "工步1",
                        OperationSummary = "设备.停止"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("same-step", "产品A", "工步1", "设备", "停止")
            }
        };

        InvokeImportSchemePackage(viewModel, package);
        viewModel.SaveSchemesCommand.Execute(null);

        BusinessConfigurationCatalog reloadedCatalog = BusinessConfigurationStore.LoadCatalog();
        WorkStepProfile existingWorkStep = reloadedCatalog.WorkSteps.Single(step => step.Id == "same-step");
        WorkStepProfile importedWorkStep = reloadedCatalog.WorkSteps.Single(step => step.StepName == "工步1_方案A");

        Assert.That(existingWorkStep.Steps.Single().InvokeMethod, Is.EqualTo("启动"));
        Assert.That(importedWorkStep.Steps.Single().InvokeMethod, Is.EqualTo("停止"));
    }

    [Test]
    public void ImportedWorkStepFallbackMatchUsesOperationSummary()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("existing-step", "产品A", "工步1", "设备", "启动")));

        SchemeConfigurationViewModel viewModel = new();
        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "missing-step",
                        ProductName = "产品A",
                        StepName = "工步1",
                        OperationSummary = "设备.停止"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("old-step", "产品A", "工步1", "设备", "启动"),
                CreateWorkStep("new-step", "产品A", "工步1", "设备", "停止")
            }
        };

        InvokeImportSchemePackage(viewModel, package);

        WorkStepProfile existingWorkStep = viewModel.WorkSteps.Single(step => step.Id == "existing-step");
        WorkStepProfile importedWorkStep = viewModel.WorkSteps.Single(step => step.StepName == "工步1_方案A");

        Assert.That(existingWorkStep.Steps.Single().InvokeMethod, Is.EqualTo("启动"));
        Assert.That(importedWorkStep.Steps.Single().InvokeMethod, Is.EqualTo("停止"));
    }

    [Test]
    public void ImportedWorkStepIdMatchMustRespectOperationSummary()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(
            new ProductProfile { ProductName = "产品A" },
            CreateWorkStep("existing-step", "产品A", "工步1", "设备", "启动")));

        SchemeConfigurationViewModel viewModel = new();
        SchemeConfigurationPackage package = new()
        {
            Product = new ProductProfile { ProductName = "产品A" },
            Scheme = new SchemeProfile
            {
                SchemeName = "方案A",
                ProductName = "产品A",
                Steps = new ObservableCollection<SchemeWorkStepItem>
                {
                    new()
                    {
                        WorkStepId = "shared-step",
                        ProductName = "产品A",
                        StepName = "工步1",
                        OperationSummary = "设备.停止"
                    }
                }
            },
            WorkSteps = new ObservableCollection<WorkStepProfile>
            {
                CreateWorkStep("shared-step", "产品A", "工步1", "设备", "启动"),
                CreateWorkStep("shared-step", "产品A", "工步1", "设备", "停止")
            }
        };

        InvokeImportSchemePackage(viewModel, package);

        WorkStepProfile existingWorkStep = viewModel.WorkSteps.Single(step => step.Id == "existing-step");
        WorkStepProfile importedWorkStep = viewModel.WorkSteps.Single(step => step.StepName == "工步1_方案A");

        Assert.That(existingWorkStep.Steps.Single().InvokeMethod, Is.EqualTo("启动"));
        Assert.That(importedWorkStep.Steps.Single().InvokeMethod, Is.EqualTo("停止"));
    }

    [Test]
    public void SystemOperationLoadsInvokeMethodRemarksAndParametersFromSystemSource()
    {
        BusinessConfigurationStore.SaveCatalog(CreateCatalog(new ProductProfile { ProductName = "产品A" }));
        WorkStepConfigurationViewModel viewModel = new();

        viewModel.NewWorkStepCommand.Execute(null);

        Assert.That(viewModel.IsOperationDrawerOpen, Is.True);
        Assert.That(viewModel.InvokeMethodOptions, Does.Contain("HextoString"));
        Assert.That(viewModel.InvokeMethodOptions, Does.Contain("StringtoHex"));
        Assert.That(viewModel.InvokeMethodRemarkOptions, Does.Contain("十六进制字符串转换为普通字符串"));
        Assert.That(viewModel.InvokeMethodRemarkOptions, Does.Contain("字符串转换为十六进制字符串"));

        viewModel.EditingInvokeMethod = "HextoString";

        Assert.That(viewModel.EditingInvokeMethodRemark, Is.EqualTo("十六进制字符串转换为普通字符串"));
        WorkStepOperationParameter parameter = viewModel.EditingInvokeParameters.Single();
        Assert.That(parameter.Description, Is.EqualTo("十六进制字符串"));
        Assert.That(parameter.Value, Is.EqualTo("string"));

        viewModel.EditingInvokeMethodRemark = "字符串转换为十六进制字符串";

        Assert.That(viewModel.EditingInvokeMethod, Is.EqualTo("StringtoHex"));
        parameter = viewModel.EditingInvokeParameters.Single();
        Assert.That(parameter.Description, Is.EqualTo("要转换的字符串"));
        Assert.That(parameter.Value, Is.EqualTo("string"));
    }

    private static BusinessConfigurationCatalog CreateCatalog(ProductProfile product, params WorkStepProfile[] workSteps)
    {
        return new BusinessConfigurationCatalog
        {
            Products = new ObservableCollection<ProductProfile> { product },
            WorkSteps = new ObservableCollection<WorkStepProfile>(workSteps)
        };
    }

    private static WorkStepProfile CreateWorkStep(
        string id,
        string productName,
        string stepName,
        string operationObject,
        string invokeMethod)
    {
        return new WorkStepProfile
        {
            Id = id,
            ProductName = productName,
            StepName = stepName,
            Steps = new ObservableCollection<WorkStepOperation>
            {
                new()
                {
                    OperationObject = operationObject,
                    InvokeMethod = invokeMethod
                }
            }
        };
    }

    private static void InvokeImportSchemePackage(
        SchemeConfigurationViewModel viewModel,
        SchemeConfigurationPackage package)
    {
        MethodInfo importMethod = typeof(SchemeConfigurationViewModel)
            .GetMethod("ImportSchemePackage", BindingFlags.Instance | BindingFlags.NonPublic)!;

        importMethod.Invoke(viewModel, new object[] { package });
    }
}
