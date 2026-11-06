using Newtonsoft.Json.Linq;
using Shared.Models.MES;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using JsonFormatting = Newtonsoft.Json.Formatting;

namespace Shared.Infrastructure.PackMethod
{
    public static class BuildMESData
    {
        /// <summary>MES 配置文件根目录。</summary>
        static string _LayoutFile = $"{System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase}\\Config\\MES_Config";
        #region Context
        /// <summary>
        /// 保存一次转换过程中的错误字段，避免使用跨请求共享的静态状态。
        /// </summary>
        public sealed class BuildErrorState
        {
            public string ErrorPath { get; set; } = string.Empty;
        }

        /// <summary>
        /// 保存单次转换的共享状态。JSON 和 SOAP 共用源数据索引、引用布局缓存、循环检测和错误路径；
        /// SOAP 额外使用默认命名空间，JSON 保持 XNamespace.None。
        /// </summary>
        public sealed class BuildContext
        {
            /// <summary>
            /// 创建单次转换上下文，并共享引用布局缓存和循环引用检测集合。
            /// </summary>
            public BuildContext(MesDataInfoTree sourceData, IReadOnlyDictionary<string, object> sourceValues,
                XNamespace defaultNamespace = null, int? whileIndex = null,
                Dictionary<string, TreeModel> referencedLayouts = null, HashSet<string> activeReferencedLayouts = null,
                BuildErrorState errorState = null)
            {
                SourceData = sourceData;
                SourceValues = sourceValues;
                DefaultNamespace = defaultNamespace ?? XNamespace.None;
                WhileIndex = whileIndex;
                ReferencedLayouts = referencedLayouts ?? new Dictionary<string, TreeModel>(StringComparer.OrdinalIgnoreCase);
                ActiveReferencedLayouts = activeReferencedLayouts ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ErrorState = errorState ?? new BuildErrorState();
            }

            /// <summary>当前转换使用的完整源数据。</summary>
            public MesDataInfoTree SourceData { get; }

            /// <summary>按客户端编码建立的源值索引。</summary>
            public IReadOnlyDictionary<string, object> SourceValues { get; }

            /// <summary>当前节点继承的默认 XML 命名空间；JSON 转换使用空命名空间。</summary>
            public XNamespace DefaultNamespace { get; }

            /// <summary>循环结构当前从一开始的序号；非循环节点为空。</summary>
            public int? WhileIndex { get; }

            /// <summary>本次转换已加载的引用布局缓存。</summary>
            public Dictionary<string, TreeModel> ReferencedLayouts { get; }

            /// <summary>当前递归路径中的引用布局，用于检测循环引用。</summary>
            public HashSet<string> ActiveReferencedLayouts { get; }

            /// <summary>本次转换共享的错误字段状态。</summary>
            public BuildErrorState ErrorState { get; }

            /// <summary>最近一次参与转换的字段上下文。</summary>
            public string ErrorPath
            {
                get => ErrorState.ErrorPath;
                set => ErrorState.ErrorPath = value ?? string.Empty;
            }

            /// <summary>
            /// 创建共享当前缓存、但切换循环序号的上下文。
            /// </summary>
            public BuildContext WithWhileIndex(int whileIndex)
            {
                return new BuildContext(SourceData, SourceValues, DefaultNamespace, whileIndex,
                    ReferencedLayouts, ActiveReferencedLayouts, ErrorState);
            }

            /// <summary>
            /// 创建共享当前缓存、但切换默认命名空间的上下文。
            /// </summary>
            public BuildContext WithDefaultNamespace(XNamespace defaultNamespace)
            {
                return new BuildContext(SourceData, SourceValues, defaultNamespace, WhileIndex,
                    ReferencedLayouts, ActiveReferencedLayouts, ErrorState);
            }
        }

        /// <summary>
        /// 为单次转换建立源值索引和引用布局上下文。
        /// </summary>
        public static BuildContext CreateBuildContext(MesDataInfoTree sourceData, XNamespace defaultNamespace = null)
        {
            Dictionary<string, object> sourceValues = new(StringComparer.Ordinal);
            foreach (MesDataInfoItem sourceItem in sourceData?.MesDataInfoItems ?? Enumerable.Empty<MesDataInfoItem>())
            {
                if (!sourceValues.ContainsKey(sourceItem.Code))
                {
                    sourceValues.Add(sourceItem.Code, sourceItem.Value);
                }
            }
            return new BuildContext(sourceData, sourceValues, defaultNamespace);
        }

        #endregion

        #region JSON
        /// <summary>
        /// 将布局树构建为 JSON 节点。内部始终传递 JToken，只有 Convert 入口负责最终序列化。
        /// </summary>
        public static JToken BuildJsonToken(IEnumerable<TreeModel> layout, BuildContext context)
        {
            IReadOnlyList<TreeModel> items = layout as IReadOnlyList<TreeModel> ?? layout.ToList();
            if (items.Count == 1 && string.IsNullOrEmpty(items[0].MESCode))
            {
                // 匿名根节点本身就是文档内容，只构建一次，避免重复执行循环字段和类型转换。
                return BuildJsonNode(items[0], context);
            }

            return BuildObject(items, context);
        }

        /// <summary>
        /// 将一个布局节点挂载到父对象。普通节点只产生一个属性；普通字段循环会产生多个动态属性，统一在此处分流。
        /// </summary>
        private static void AppendJsonNode(JObject parent, TreeModel node, BuildContext context)
        {
            JToken token = BuildJsonNode(node, context);
            if (token == null) return;

            // 普通循环字段一次生成多个动态属性，直接合并到当前父对象；其他节点仍以 MESCode 挂载。
            if (node.IsWhile && context.WhileIndex == null && token is JObject whileFields)
            {
                foreach (JProperty property in whileFields.Properties().ToList())
                {
                    property.Remove();
                    parent.Add(property);
                }
                return;
            }
            parent.Add(node.MESCode, token);
        }

        /// <summary>
        /// 根据当前节点自身类型构建 JSON 内容，所有结构类型的判断集中在一个分派点中。
        /// </summary>
        private static JToken BuildJsonNode(TreeModel node, BuildContext context)
        {
            string dataType = node.DataType?.ToUpperInvariant() ?? string.Empty;
            return dataType switch
            {
                "MODEL" when node.IsWhile && context.WhileIndex == null => BuildWhileModels(node, context),
                "MODEL" => BuildObject(node.Children, context),
                "LIST" => BuildJsonList(node.Children, context),
                "ARRAY" => BuildJsonArray(node.Children, context),
                "JSON" => BuildReferencedJson(node, context),
                "STEPMODEL" => BuildStepModels(node, context),
                "STRING" when node.Children != null && node.Children.Count > 0 =>
                    new JValue(BuildJsonToken(node.Children, context).ToString(JsonFormatting.None)),
                _ when node.Children != null && node.Children.Count > 0 =>
                    BuildJsonToken(node.Children, context),
                _ when node.IsWhile && context.WhileIndex == null => BuildWhileFields(node, context),
                _ => BuildScalarJsonValue(node, context)
            };
        }

        /// <summary>
        /// 构建不包含子结构的 JSON 标量节点。
        /// </summary>
        private static JToken BuildScalarJsonValue(TreeModel node, BuildContext context)
        {
            object value = ResolveSourceValue(context, node);
            if (!node.IsNull && string.IsNullOrEmpty(value?.ToString())) return null;
            return BuildJsonValue(value, node, context);
        }

        /// <summary>
        /// 构建外部 JSON 布局，并保持原有“作为 JSON 字符串写入当前字段”的配置语义。
        /// </summary>
        private static JToken BuildReferencedJson(TreeModel node, BuildContext context)
        {
            if (!context.ActiveReferencedLayouts.Add(node.ClientCode))
            {
                throw new InvalidOperationException($"JSON 布局存在循环引用：{node.ClientCode}");
            }
            try
            {
                if (!context.ReferencedLayouts.TryGetValue(node.ClientCode, out TreeModel referencedLayout))
                {
                    referencedLayout = JsonHelper.ReadJson<TreeModel>($"{_LayoutFile}\\MESConvertConfig\\{node.ClientCode}.json");
                    context.ReferencedLayouts.Add(node.ClientCode, referencedLayout);
                }
                return new JValue(BuildJsonToken(referencedLayout.Children, context).ToString(JsonFormatting.None));
            }
            finally
            {
                context.ActiveReferencedLayouts.Remove(node.ClientCode);
            }
        }

        /// <summary>
        /// 展开普通字段循环。字段名在每轮继续递进，并将最后生成的名称保留到布局节点中。
        /// </summary>
        private static JObject BuildWhileFields(TreeModel node, BuildContext context)
        {
            JObject fields = new();
            string mesCode = node.MESCode;
            for (int i = 1; i <= node.WhileCount; i++)
            {
                BuildContext whileContext = context.WithWhileIndex(i);
                object value = ResolveSourceValue(whileContext, node);
                if (!node.IsNull && string.IsNullOrEmpty(value?.ToString())) continue;

                string generatedMesCode = GetWhileName(i, mesCode);
                fields.Add(generatedMesCode, BuildJsonValue(value, node, context));
            }
            return fields;
        }

        /// <summary>
        /// 将普通字段转换为 JSON 值节点。该方法不再感知父对象，也不负责属性挂载。
        /// </summary>
        private static JToken BuildJsonValue(object value, TreeModel layout, BuildContext context)
        {
            context.ErrorPath = $"ClientName:{layout.ClientCode} MesName:{layout.MESCode} Value:{value}";
            if (!string.IsNullOrEmpty(layout.JudgeValue))
            {
                value = layout.JudgeValue.Equals(value) ? layout.OKText : layout.NGText;
            }

            switch (layout.DataType?.ToUpperInvariant())
            {
                case "STRING":
                    return new JValue(value?.ToString() ?? string.Empty);
                case "BOOL":
                    bool boolValue;
                    if (!bool.TryParse(value?.ToString(), out boolValue))
                    {
                        boolValue = string.Equals(value?.ToString(), layout.JudgeValue, StringComparison.OrdinalIgnoreCase);
                    }
                    return new JValue(boolValue);
                case "DATETIME":
                    if (!DateTime.TryParse(value?.ToString(), out _)) value = DateTime.Now.ToString();
                    return new JValue(System.Convert.ToDateTime(value).ToString(layout.DefaultValue));
                case "TIMETICKS13":
                    if (string.IsNullOrEmpty(value?.ToString())) value = DateTime.Now.ToString();
                    DateTime time = System.Convert.ToDateTime(value);
                    DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1, 0, 0, 0, 0));
                    return new JValue((time.Ticks - startTime.Ticks) / 10000);
                case "INT":
                    return new JValue(System.Convert.ToInt32(value));
                case "DOUBLE":
                    return new JValue(Math.Round(System.Convert.ToDouble(value), System.Convert.ToInt32(layout.KeepDecimalLength)));
                default:
                    return JValue.CreateNull();
            }
        }

        /// <summary>
        /// 将数组布局转换为 JSON 数组，支持分隔文本和子模型两种来源。
        /// </summary>
        private static JArray BuildJsonArray(List<TreeModel> layout, BuildContext context)
        {
            JArray result = new();
            foreach (TreeModel item in layout)
            {
                // 非循环字段允许通过 ClientCode 中的 [分隔符] 将一个源值展开成多个数组元素。
                if (!item.IsWhile && TryAppendDelimitedArrayValues(result, item, context))
                {
                    continue;
                }

                if (item.IsWhile && context.WhileIndex == null)
                {
                    for (int i = 1; i <= item.WhileCount; i++)
                    {
                        JToken token = BuildJsonNode(item, context.WithWhileIndex(i));
                        if (token == null) break;
                        result.Add(token);
                    }
                    continue;
                }

                JToken itemToken = BuildJsonNode(item, context);
                if (itemToken != null) result.Add(itemToken);
            }
            return result;
        }

        /// <summary>
        /// 尝试按配置分隔符拆分单个源值，并追加到目标 JSON 数组。
        /// </summary>
        private static bool TryAppendDelimitedArrayValues(JArray result, TreeModel item, BuildContext context)
        {
            if (item.Children.Count > 0 || string.IsNullOrEmpty(item.ClientCode)) return false;
            int openIndex = item.ClientCode.IndexOf('[');
            int closeIndex = item.ClientCode.IndexOf(']', openIndex + 1);
            if (openIndex < 0 || closeIndex != openIndex + 2) return false;

            char separator = item.ClientCode[openIndex + 1];
            string sourceCode = item.ClientCode.Remove(openIndex, closeIndex - openIndex + 1);
            object value = ResolveSourceValue(context, item, sourceCode);
            string text = value?.ToString() ?? string.Empty;
            if (!text.Contains(separator))
            {
                result.Add(BuildJsonValue(value, item, context));
                return true;
            }

            foreach (string part in text.Split(separator)) result.Add(part);
            return true;
        }

        /// <summary>
        /// 将列表布局的子节点依次转换为 JSON 数组元素。
        /// </summary>
        private static JArray BuildJsonList(List<TreeModel> layout, BuildContext context)
        {
            JArray result = new();
            foreach (TreeModel item in layout)
            {
                JToken token = BuildJsonNode(item, context);
                if (token == null) continue;

                // 循环 Model 和匿名数组节点代表多个列表元素，需要平铺到当前列表。
                if (token is JArray multipleItems && (item.IsWhile || string.IsNullOrEmpty(item.MESCode)))
                {
                    foreach (JToken child in multipleItems.Children().ToList())
                    {
                        child.Remove();
                        result.Add(child);
                    }
                    continue;
                }

                // LIST 中的普通值保持原格式，以 MESCode 包装成单属性对象。
                if (token is JValue || token is JArray)
                {
                    result.Add(new JObject { [item.MESCode ?? string.Empty] = token });
                    continue;
                }
                result.Add(token);
            }
            return result;
        }
        /// <summary>
        /// 按 WhileCount 指定的数量构建循环 Model。
        /// 每一组先完成全部字段取值和必填校验，通过后再整体写入，避免输出只有部分字段的 Model。
        /// </summary>
        private static JArray BuildWhileModels(TreeModel layout, BuildContext context)
        {
            JArray models = new();
            for (int i = 1; i <= layout.WhileCount; i++)
            {
                BuildContext whileContext = context.WithWhileIndex(i);
                JObject model = BuildObject(layout.Children, whileContext);
                bool missingRequiredField = layout.Children.Any(field =>
                    field.Children.Count == 0 &&
                    !field.IsNull &&
                    !model.ContainsKey(field.MESCode));
                if (missingRequiredField)
                {
                    break;
                }
                models.Add(model);
            }
            return models;
        }

        /// <summary>
        /// 将模型布局的全部子字段构建为 JSON 对象。
        /// </summary>
        private static JObject BuildObject(IEnumerable<TreeModel> layout, BuildContext context)
        {
            JObject result = new();
            foreach (TreeModel item in layout)
            {
                AppendJsonNode(result, item, context);
            }
            return result;
        }

        /// <summary>
        /// 根据步骤名称数据构建步骤 Model 列表。特殊的步骤字段定位规则留在该结构方法内，不参与类型分派。
        /// </summary>
        private static JArray BuildStepModels(TreeModel layout, BuildContext context)
        {
            JArray models = new();
            IEnumerable<MesDataInfoItem> stepNames = context.SourceData?.MesDataInfoItems?
                .Where(item => item.Code.Contains("_StepName", StringComparison.OrdinalIgnoreCase))
                ?? Enumerable.Empty<MesDataInfoItem>();
            foreach (MesDataInfoItem stepName in stepNames)
            {
                JObject model = new();
                foreach (TreeModel field in layout.Children)
                {
                    string clientCode = stepName.Code.Replace("_StepName", $"_{field.ClientCode}");
                    object value = ResolveSourceValue(context, field, clientCode);
                    if (!field.IsNull && string.IsNullOrEmpty(value?.ToString())) continue;
                    model.Add(field.MESCode, BuildJsonValue(value, field, context));
                }
                models.Add(model);
            }
            return models;
        }

        /// <summary>
        /// 按 ClientCode 从单次转换索引取值，并保持原有的空值回退和循环默认值规则。
        /// </summary>
        private static object ResolveSourceValue(BuildContext context, TreeModel node, string clientCode = null)
        {
            string resolvedClientCode = clientCode ?? node.ClientCode ?? string.Empty;
            if (clientCode == null && context.WhileIndex.HasValue)
            {
                resolvedClientCode = GetWhileName(context.WhileIndex.Value, resolvedClientCode);
            }
            context.SourceValues.TryGetValue(resolvedClientCode, out object value);
            if (!string.IsNullOrEmpty(value?.ToString())) return value;
            if (context.WhileIndex.HasValue && !string.IsNullOrEmpty(node.DefaultValue) && node.DefaultValue.Contains("["))
            {
                return GetWhileName(context.WhileIndex.Value, node.DefaultValue);
            }
            return node.DefaultValue;
        }

        /// <summary>
        /// 根据循环序号替换客户端字段名称中的方括号数字占位。
        /// </summary>
        private static string GetWhileName(int index, string name)
        {
            index--;
            string result = null;
            if (name.Contains("["))
            {
                int startIndex = name.IndexOf("[") + 1;
                int length = name.IndexOf("]") - startIndex;
                string clientValue = name.Substring(startIndex, length);
                int value = System.Convert.ToInt32(clientValue) + index;
                result = name.Remove(startIndex, length).Insert(startIndex, value.ToString().PadLeft(length, '0')).Replace("[", "").Replace("]", "");
            }
            else
            {
                result = name;
            }
            return result;
        }
        #endregion
        #region SOAP
        /// <summary>
        /// SOAP 结构的唯一分派点。结构方法只负责自己的 XML 语义，不再重复判断全部 DataType。
        /// </summary>
        private static void AppendSoapNode(XElement parent, TreeModel node, BuildContext context)
        {
            string dataType = node.DataType?.ToUpperInvariant() ?? string.Empty;
            switch (dataType)
            {
                case "MODEL" when node.IsWhile && context.WhileIndex == null:
                    foreach (XElement model in BuildWhileSoapModels(node, context)) parent.Add(model);
                    return;
                case "MODEL":
                case "LIST":
                    parent.Add(BuildSoapElement(node, context));
                    return;
                case "STEPMODEL":
                    foreach (XElement model in BuildStepSoapModels(node, context)) parent.Add(model);
                    return;
                case "XMLNAMESPACE":
                    AddSoapNamespaceDeclaration(parent, node);
                    return;
                case "XMLPROPERTY":
                    AddSoapProperty(parent, context, node);
                    return;
                case "NULL":
                    parent.Add(BuildSoapNullElement(node, context));
                    return;
                case "JSON":
                    parent.Add(BuildReferencedSoap(node, context));
                    return;
                default:
                    XElement valueElement = BuildSoapValueElement(node, context);
                    if (valueElement != null) parent.Add(valueElement);
                    return;
            }
        }

        /// <summary>
        /// 构建 SOAP 模型、列表或根元素。
        /// 模型和列表在 SOAP 中都表现为带命名空间的元素容器，因此共用同一套递归逻辑。
        /// </summary>
        public static XElement BuildSoapElement(TreeModel node, BuildContext context)
        {
            XNamespace nodeNamespace = ResolveSoapNamespace(node, context);
            XElement element = new(nodeNamespace + node.MESCode);
            BuildContext childContext = context.WithDefaultNamespace(nodeNamespace);
            foreach (TreeModel child in node.Children)
            {
                AppendSoapNode(element, child, childContext);
            }

            return element;
        }

        /// <summary>
        /// 按循环次数构建 SOAP 模型，遇到缺失的必填字段时停止继续生成。
        /// </summary>
        private static IEnumerable<XElement> BuildWhileSoapModels(TreeModel model, BuildContext context)
        {
            for (int i = 1; i <= model.WhileCount; i++)
            {
                BuildContext whileContext = context.WithWhileIndex(i);
                bool missingRequiredField = model.Children.Any(child =>
                    child.Children.Count == 0 &&
                    !child.IsNull &&
                    string.IsNullOrEmpty(ResolveSoapValue(child, whileContext)?.ToString()));
                if (missingRequiredField) yield break;
                yield return BuildSoapElement(model, whileContext);
            }
        }

        /// <summary>
        /// 按 JSON StepModel 的业务名称定位规则构建重复的 SOAP 业务模型元素。
        /// </summary>
        private static IEnumerable<XElement> BuildStepSoapModels(TreeModel layout, BuildContext context)
        {
            IEnumerable<MesDataInfoItem> stepNames = context.SourceData?.MesDataInfoItems?
                .Where(item => item.Code.Contains("_StepName", StringComparison.OrdinalIgnoreCase))
                ?? Enumerable.Empty<MesDataInfoItem>();
            XNamespace modelNamespace = ResolveSoapNamespace(layout, context);
            BuildContext modelContext = context.WithDefaultNamespace(modelNamespace);
            foreach (MesDataInfoItem stepName in stepNames)
            {
                XElement model = new(modelNamespace + layout.MESCode);
                foreach (TreeModel field in layout.Children)
                {
                    string clientCode = stepName.Code.Replace("_StepName", $"_{field.ClientCode}");
                    XElement fieldElement = BuildSoapValueElement(field, modelContext, clientCode);
                    if (fieldElement != null) model.Add(fieldElement);
                }
                yield return model;
            }
        }

        /// <summary>
        /// 解析并转换标量值，生成带正确命名空间的 SOAP 元素。
        /// </summary>
        private static XElement BuildSoapValueElement(TreeModel node, BuildContext context, string clientCode = null)
        {
            object value = ResolveSoapValue(node, context, clientCode);
            
            if (!node.IsNull && string.IsNullOrEmpty(value?.ToString())) return null;
            XNamespace nodeNamespace = ResolveSoapNamespace(node, context);
            return new XElement(nodeNamespace + node.MESCode, ConvertSoapValue(value, node, context));
        }
        /// <summary>
        /// 生成带正确命名空间的 SOAP NULL元素。
        /// </summary>
        private static XElement BuildSoapNullElement(TreeModel node, BuildContext context)
        {
            return new XElement(ResolveSoapNamespace(node, context) + node.MESCode);
        }

        /// <summary>
        /// 按节点数据类型和判断映射规则转换 SOAP 标量值。
        /// </summary>
        private static object ConvertSoapValue(object value, TreeModel node, BuildContext context)
        {
            context.ErrorPath = $"ClientName:{node.ClientCode} MESName:{node.MESCode} Value:{value}";
            if (!string.IsNullOrEmpty(node.JudgeValue))
            {
                value = string.Equals(node.JudgeValue, value?.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? node.OKText
                    : node.NGText;
            }
            return node.DataType?.ToUpperInvariant() switch
            {
                "STRING" => value?.ToString() ?? string.Empty,
                "INT" => System.Convert.ToInt32(value),
                "DOUBLE" or "DOUBULE" => Math.Round(System.Convert.ToDouble(value), System.Convert.ToInt32(node.KeepDecimalLength)),
                "DATETIME" => System.Convert.ToDateTime(string.IsNullOrEmpty(value?.ToString()) ? DateTime.Now : value).ToString(node.DefaultValue),
                _ => value?.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// 加载引用布局、构建对应 JSON 文本并包装为 SOAP 子元素。
        /// </summary>
        private static XElement BuildReferencedSoap(TreeModel node, BuildContext context)
        {
            if (!context.ActiveReferencedLayouts.Add(node.ClientCode))
            {
                throw new InvalidOperationException($"SOAP 引用布局存在循环引用：{node.ClientCode}");
            }
            try
            {
                if (!context.ReferencedLayouts.TryGetValue(node.ClientCode, out TreeModel referencedLayout))
                {
                    referencedLayout = JsonHelper.ReadJson<TreeModel>($"{_LayoutFile}\\MESConvertConfig\\{node.ClientCode}.json");
                    context.ReferencedLayouts.Add(node.ClientCode, referencedLayout);
                }
                JToken json = BuildJsonToken(referencedLayout.Children, context);
                return new XElement(ResolveSoapNamespace(node, context) + node.MESCode, json.ToString(JsonFormatting.None));
            }
            finally
            {
                context.ActiveReferencedLayouts.Remove(node.ClientCode);
            }
        }

        /// <summary>
        /// 从源值索引解析 SOAP 字段值，并处理循环字段和默认值。
        /// </summary>
        private static object ResolveSoapValue(TreeModel node, BuildContext context, string clientCode = null)
        {
            string resolvedClientCode = clientCode ?? node.ClientCode ?? string.Empty;
            if (clientCode == null && context.WhileIndex.HasValue)
            {
                resolvedClientCode = GetWhileName(context.WhileIndex.Value, resolvedClientCode);
            }
            context.SourceValues.TryGetValue(resolvedClientCode, out object value);
            if (!string.IsNullOrEmpty(value?.ToString())) return value;
            if (context.WhileIndex.HasValue && !string.IsNullOrEmpty(node.DefaultValue) && node.DefaultValue.Contains("["))
            {
                return GetWhileName(context.WhileIndex.Value, node.DefaultValue);
            }
            return node.DefaultValue;
        }

        /// <summary>
        /// 获取节点显式命名空间；未配置时继承上下文默认命名空间。
        /// </summary>
        private static XNamespace ResolveSoapNamespace(TreeModel node, BuildContext context)
        {
            return node.XMLNamespace;//string.IsNullOrWhiteSpace(node.XMLNameSpace) ? context.DefaultNamespace : node.XMLNameSpace;
        }

        /// <summary>
        /// 向父元素追加配置的 XML 命名空间声明。
        /// </summary>
        private static void AddSoapNamespaceDeclaration(XElement parent, TreeModel node)
        {
            if (string.IsNullOrWhiteSpace(node.MESCode) || string.IsNullOrWhiteSpace(node.XMLNamespace)) return;
            parent.Add(new XAttribute(XNamespace.Xmlns + node.MESCode, node.XMLNamespace));
        }

        /// <summary>
        /// 向父元素追加配置的 XML 属性字段。
        /// </summary>
        private static void AddSoapProperty(XElement parent, BuildContext context, TreeModel node)
        {
            object value = ResolveSoapValue(node, context);
            if (!string.IsNullOrEmpty(node.JudgeValue))
            {
                value = node.JudgeValue.Equals(value) ? node.OKText : node.NGText;
            }
            XAttribute xAttribute1 = new XAttribute($"{node.MESCode}", value ?? "");
            parent.Add(xAttribute1);
        }
        #endregion
        #region JOINT
        /// <summary>
        /// 按结构配置顺序拼接源数据；普通字段保留字段分隔符，循环字段只在循环值之间插入分隔符。
        /// </summary>
        public static string ItemsToString(MesDataInfoTree sourceData, IEnumerable<TreeModel> layout)
        {
            // 单次建立精确匹配索引，保持旧逻辑 FirstOrDefault 在重复编码时取首项的行为。
            Dictionary<string, object> sourceValues = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (MesDataInfoItem sourceItem in sourceData?.MesDataInfoItems ?? Enumerable.Empty<MesDataInfoItem>())
            {
                if (!sourceValues.ContainsKey(sourceItem.Code))
                {
                    sourceValues.Add(sourceItem.Code, sourceItem.Value);
                }
            }

            StringBuilder result = new StringBuilder();
            foreach (TreeModel item in layout ?? Enumerable.Empty<TreeModel>())
            {
                string separator = item.MESCode ?? string.Empty;
                int valueCount = item.IsWhile ? item.WhileCount : 1;
                bool hasWhileValue = false;

                // 普通字段和循环字段共用同一条取值路径；差异只保留在字段编码、格式化方式和末尾分隔符。
                for (int index = 1; index <= valueCount; index++)
                {
                    string clientCode = item.IsWhile
                        ? (item.ClientCode ?? string.Empty).Replace("_", $"{index}_")
                        : item.ClientCode ?? string.Empty;
                    sourceValues.TryGetValue(clientCode, out object value);

                    bool isDateTime = !item.IsWhile &&
                        (item.DataType ?? string.Empty).Contains("DATETIME", StringComparison.OrdinalIgnoreCase);
                    if (!isDateTime && string.IsNullOrEmpty(value?.ToString()))
                    {
                        value = item.DefaultValue;
                    }

                    if (!item.IsNull && string.IsNullOrEmpty(value?.ToString()))
                    {
                        continue;
                    }

                    if (item.IsWhile)
                    {
                        if (hasWhileValue)
                        {
                            result.Append(separator);
                        }

                        result.Append(value?.ToString());
                        hasWhileValue = true;
                        continue;
                    }

                    if (isDateTime)
                    {
                        result.Append(System.Convert.ToDateTime(value).ToString(item.DefaultValue));
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(item.JudgeValue))
                        {
                            value = string.Equals(item.JudgeValue, value?.ToString(), StringComparison.Ordinal)
                                ? item.OKText
                                : item.NGText;
                        }

                        result.Append(value?.ToString());
                    }

                    // 兼容原有 JOINT 规则：普通字段始终追加字段分隔符，循环字段只在值之间追加。
                    result.Append(separator);
                }
            }
            return result.ToString();
        }
        #endregion
    }
}
