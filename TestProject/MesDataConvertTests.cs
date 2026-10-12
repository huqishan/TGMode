using Newtonsoft.Json.Linq;
using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System.Xml.Linq;

namespace TestProject;

/// <summary>
/// MES JSON 布局转换回归测试，重点覆盖节点构建、根列表和循环字段名递进。
/// </summary>
public class MesDataConvertTests
{
    #region JSON 节点构建

    [Test]
    public void Convert_普通字段_应从源数据索引生成Json对象()
    {
        MesDataInfoTree sourceData = CreateSourceData(new MesDataInfoItem("ClientValue", "OK"));
        DataSruct layout = CreateJsonLayout(new TreeModel
        {
            ClientCode = "ClientValue",
            MESCode = "MesValue",
            DataType = "STRING",
            IsNull = true
        });

        JObject result = JObject.Parse(MesDataConvert.Convert(sourceData, layout));

        Assert.That(result.Value<string>("MesValue"), Is.EqualTo("OK"));
    }

    [Test]
    public void Convert_类型转换失败_应返回当前字段上下文()
    {
        MesDataInfoTree sourceData = CreateSourceData(new MesDataInfoItem("ClientValue", "not-number"));
        DataSruct layout = CreateJsonLayout(new TreeModel
        {
            ClientCode = "ClientValue",
            MESCode = "MesValue",
            DataType = "INT",
            IsNull = true
        });

        Exception exception = Assert.Throws<Exception>(() => MesDataConvert.Convert(sourceData, layout));

        Assert.That(exception.Message, Does.Contain("ClientName:ClientValue MesName:MesValue Value:not-number"));
    }

    [Test]
    public void Convert_根节点为无名称List_应直接生成Json数组()
    {
        MesDataInfoTree sourceData = CreateSourceData(new MesDataInfoItem("ClientValue", 12));
        DataSruct layout = CreateJsonLayout(new TreeModel
        {
            MESCode = string.Empty,
            DataType = "LIST",
            Children = new List<TreeModel>
            {
                new()
                {
                    ClientCode = "ClientValue",
                    MESCode = "MesValue",
                    DataType = "INT",
                    IsNull = true
                }
            }
        });

        JArray result = JArray.Parse(MesDataConvert.Convert(sourceData, layout));

        Assert.That(result[0]?["MesValue"]?.Value<int>(), Is.EqualTo(12));
    }

    [Test]
    public void Convert_Model嵌套List和Array_应通过统一节点分派生成结构()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Name", "Device"),
            new MesDataInfoItem("ItemValue", 7),
            new MesDataInfoItem("ArrayValue", 9));
        DataSruct layout = CreateJsonLayout(new TreeModel
        {
            MESCode = "Payload",
            DataType = "MODEL",
            Children = new List<TreeModel>
            {
                new() { ClientCode = "Name", MESCode = "Name", DataType = "STRING", IsNull = true },
                new()
                {
                    MESCode = "Items",
                    DataType = "LIST",
                    Children = new List<TreeModel>
                    {
                        new()
                        {
                            DataType = "MODEL",
                            Children = new List<TreeModel>
                            {
                                new() { ClientCode = "ItemValue", MESCode = "Value", DataType = "INT", IsNull = true }
                            }
                        }
                    }
                },
                new()
                {
                    MESCode = "Values",
                    DataType = "ARRAY",
                    Children = new List<TreeModel>
                    {
                        new() { ClientCode = "ArrayValue", DataType = "INT", IsNull = true }
                    }
                }
            }
        });

        JObject result = JObject.Parse(MesDataConvert.Convert(sourceData, layout));

        Assert.Multiple(() =>
        {
            Assert.That(result["Payload"]?["Name"]?.Value<string>(), Is.EqualTo("Device"));
            Assert.That(result["Payload"]?["Items"]?[0]?["Value"]?.Value<int>(), Is.EqualTo(7));
            Assert.That(result["Payload"]?["Values"]?[0]?.Value<int>(), Is.EqualTo(9));
        });
    }

    #endregion

    #region 循环字段名

    [Test]
    public void Convert_循环字段_应基于布局字段名连续生成并保留最后结果()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Client01", "A"),
            new MesDataInfoItem("Client02", "B"));
        TreeModel whileField = new()
        {
            ClientCode = "Client[01]",
            MESCode = "Mes[01]",
            DataType = "STRING",
            IsWhile = true,
            WhileCount = 2,
            IsNull = true
        };

        JObject result = JObject.Parse(MesDataConvert.Convert(sourceData, CreateJsonLayout(whileField)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Value<string>("Mes01"), Is.EqualTo("A"));
            Assert.That(result.Value<string>("Mes02"), Is.EqualTo("B"));
            Assert.That(whileField.MESCode, Is.EqualTo("Mes[01]"));
        });
    }

    [Test]
    public void Convert_循环Model_应按WhileCount生成指定数量()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Value01", "A"),
            new MesDataInfoItem("Value02", "B"));
        DataSruct layout = CreateJsonLayout(new TreeModel
        {
            MESCode = string.Empty,
            DataType = "LIST",
            Children = new List<TreeModel>
            {
                new()
                {
                    DataType = "MODEL",
                    IsWhile = true,
                    WhileCount = 2,
                    Children = new List<TreeModel>
                    {
                        new()
                        {
                            ClientCode = "Value[01]",
                            MESCode = "Value",
                            DataType = "STRING",
                            IsNull = false
                        }
                    }
                }
            }
        });

        JArray result = JArray.Parse(MesDataConvert.Convert(sourceData, layout));

        Assert.Multiple(() =>
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0]?["Value"]?.Value<string>(), Is.EqualTo("A"));
            Assert.That(result[1]?["Value"]?.Value<string>(), Is.EqualTo("B"));
        });
    }

    [Test]
    public void Convert_循环Model必填数据不足_不应输出半成品Model()
    {
        MesDataInfoTree sourceData = CreateSourceData(new MesDataInfoItem("Required01", "A"));
        DataSruct layout = CreateJsonLayout(new TreeModel
        {
            MESCode = string.Empty,
            DataType = "LIST",
            Children = new List<TreeModel>
            {
                new()
                {
                    DataType = "MODEL",
                    IsWhile = true,
                    WhileCount = 2,
                    Children = new List<TreeModel>
                    {
                        new()
                        {
                            ClientCode = "Required[01]",
                            MESCode = "Required",
                            DataType = "STRING",
                            IsNull = false
                        },
                        new()
                        {
                            ClientCode = "Optional[01]",
                            MESCode = "Optional",
                            DataType = "STRING",
                            IsNull = true
                        }
                    }
                }
            }
        });

        JArray result = JArray.Parse(MesDataConvert.Convert(sourceData, layout));

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0]?["Required"]?.Value<string>(), Is.EqualTo("A"));
    }

    #endregion

    #region SOAP 节点构建

    [Test]
    public void Convert_SOAP嵌套模型_应完整挂载子节点并继承命名空间()
    {
        MesDataInfoTree sourceData = CreateSourceData(new MesDataInfoItem("ClientValue", "OK"));
        DataSruct layout = CreateSoapLayout(new TreeModel
        {
            MESCode = "Envelope",
            DataType = "MODEL",
            XMLNamespace = "urn:test",
            Children = new List<TreeModel>
            {
                new()
                {
                    MESCode = "Body",
                    DataType = "MODEL",
                    Children = new List<TreeModel>
                    {
                        new() { ClientCode = "ClientValue", MESCode = "Result", DataType = "STRING", IsNull = true }
                    }
                }
            }
        });

        XElement result = XElement.Parse(MesDataConvert.Convert(sourceData, layout));
        XNamespace ns = "urn:test";

        Assert.That(result.Element(ns + "Body")?.Element(ns + "Result")?.Value, Is.EqualTo("OK"));
    }

    [Test]
    public void Convert_SOAP循环模型_应按WhileCount生成指定数量()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Value01", "A"),
            new MesDataInfoItem("Value02", "B"));
        DataSruct layout = CreateSoapLayout(new TreeModel
        {
            MESCode = "Root",
            DataType = "MODEL",
            Children = new List<TreeModel>
            {
                new()
                {
                    MESCode = "Item",
                    DataType = "MODEL",
                    IsWhile = true,
                    WhileCount = 2,
                    Children = new List<TreeModel>
                    {
                        new() { ClientCode = "Value[01]", MESCode = "Value", DataType = "STRING", IsNull = false }
                    }
                }
            }
        });

        XElement result = XElement.Parse(MesDataConvert.Convert(sourceData, layout));
        List<XElement> items = result.Elements("Item").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Element("Value")?.Value, Is.EqualTo("A"));
            Assert.That(items[1].Element("Value")?.Value, Is.EqualTo("B"));
        });
    }

    [Test]
    public void Convert_SOAP日期字段_应直接按配置格式输出()
    {
        MesDataInfoTree sourceData = CreateSourceData(new MesDataInfoItem("CreateTime", new DateTime(2026, 8, 10, 13, 14, 15)));
        DataSruct layout = CreateSoapLayout(new TreeModel
        {
            MESCode = "Root",
            DataType = "MODEL",
            Children = new List<TreeModel>
            {
                new()
                {
                    ClientCode = "CreateTime",
                    MESCode = "CreateTime",
                    DataType = "DATETIME",
                    DefaultValue = "yyyyMMddHHmmss",
                    IsNull = true
                }
            }
        });

        XElement result = XElement.Parse(MesDataConvert.Convert(sourceData, layout));

        Assert.That(result.Element("CreateTime")?.Value, Is.EqualTo("20260810131415"));
    }

    /// <summary>
    /// 验证 SOAP StepModel 按工步名称展开，并沿用字段映射、空值过滤和父级命名空间。
    /// </summary>
    [Test]
    public void Convert_SOAPStepModel_应按工步生成重复元素并映射字段()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Step1_StepName", "Assembly"),
            new MesDataInfoItem("Step1_Result", "1"),
            new MesDataInfoItem("Step1_Optional", "Present"),
            new MesDataInfoItem("Step2_StepName", "Inspection"),
            new MesDataInfoItem("Step2_Result", "0"));
        DataSruct layout = CreateSoapLayout(new TreeModel
        {
            MESCode = "Root",
            DataType = "MODEL",
            XMLNamespace = "urn:test",
            Children = new List<TreeModel>
            {
                new()
                {
                    MESCode = "Step",
                    DataType = "StepModel",
                    Children = new List<TreeModel>
                    {
                        new()
                        {
                            ClientCode = "Result",
                            MESCode = "Outcome",
                            DataType = "STRING",
                            IsNull = true,
                            JudgeValue = "1",
                            OKText = "PASS",
                            NGText = "FAIL"
                        },
                        new()
                        {
                            ClientCode = "Optional",
                            MESCode = "Optional",
                            DataType = "STRING",
                            IsNull = false
                        }
                    }
                }
            }
        });

        XElement result = XElement.Parse(MesDataConvert.Convert(sourceData, layout));
        XNamespace ns = "urn:test";
        List<XElement> steps = result.Elements(ns + "Step").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(steps, Has.Count.EqualTo(2));
            Assert.That(steps[0].Element(ns + "Outcome")?.Value, Is.EqualTo("PASS"));
            Assert.That(steps[0].Element(ns + "Optional")?.Value, Is.EqualTo("Present"));
            Assert.That(steps[1].Element(ns + "Outcome")?.Value, Is.EqualTo("FAIL"));
            Assert.That(steps[1].Element(ns + "Optional"), Is.Null);
        });
    }

    #endregion

    #region JOINT 字段拼接

    /// <summary>
    /// 验证普通字段、判断文本映射和循环字段按照布局顺序生成兼容的拼接结果。
    /// </summary>
    [Test]
    public void Convert_Joint布局_应按顺序拼接普通字段与循环字段()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Code", "A"),
            new MesDataInfoItem("Result", "1"),
            new MesDataInfoItem("Value1_", "X"),
            new MesDataInfoItem("Value2_", "Y"));
        DataSruct layout = new()
        {
            StructureType = "JOINT",
            Structure = new List<TreeModel>
            {
                new() { ClientCode = "Code", MESCode = "|", DataType = "STRING", IsNull = true },
                new()
                {
                    ClientCode = "Result",
                    MESCode = "|",
                    DataType = "STRING",
                    IsNull = true,
                    JudgeValue = "1",
                    OKText = "PASS",
                    NGText = "FAIL"
                },
                new()
                {
                    ClientCode = "Value_",
                    MESCode = ",",
                    DataType = "STRING",
                    IsNull = true,
                    IsWhile = true,
                    WhileCount = 2
                }
            }
        };

        string result = MesDataConvert.Convert(sourceData, layout);

        Assert.That(result, Is.EqualTo("A|PASS|X,Y"));
    }

    [Test]
    public void Convert_Joint日期字段_应按配置格式输出并追加分隔符()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("CreateTime", new DateTime(2026, 8, 10, 13, 14, 15)));
        DataSruct layout = new()
        {
            StructureType = "JOINT",
            Structure = new List<TreeModel>
            {
                new()
                {
                    ClientCode = "CreateTime",
                    MESCode = "|",
                    DataType = "DATETIME",
                    DefaultValue = "yyyyMMddHHmmss",
                    IsNull = true
                }
            }
        };

        string result = MesDataConvert.Convert(sourceData, layout);

        Assert.That(result, Is.EqualTo("20260810131415|"));
    }

    [Test]
    public void Convert_Joint循环字段_空值跳过且仅在有效值之间追加分隔符()
    {
        MesDataInfoTree sourceData = CreateSourceData(
            new MesDataInfoItem("Value1_", "A"),
            new MesDataInfoItem("Value3_", "B"));
        DataSruct layout = new()
        {
            StructureType = "JOINT",
            Structure = new List<TreeModel>
            {
                new()
                {
                    ClientCode = "Value_",
                    MESCode = ",",
                    DataType = "STRING",
                    IsWhile = true,
                    WhileCount = 3,
                    IsNull = false
                }
            }
        };

        string result = MesDataConvert.Convert(sourceData, layout);

        Assert.That(result, Is.EqualTo("A,B"));
    }

    #endregion

    private static MesDataInfoTree CreateSourceData(params MesDataInfoItem[] items)
    {
        return new MesDataInfoTree("Product", true, "Api", items.ToList());
    }

    private static DataSruct CreateJsonLayout(params TreeModel[] nodes)
    {
        return new DataSruct
        {
            StructureType = "JSON",
            Structure = nodes.ToList()
        };
    }

    private static DataSruct CreateSoapLayout(TreeModel root)
    {
        return new DataSruct
        {
            StructureType = "SOAP",
            Structure = new List<TreeModel> { root }
        };
    }
}
