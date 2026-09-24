using Newtonsoft.Json.Linq;
using JsonFormatting = Newtonsoft.Json.Formatting;
using Shared.Abstractions.ICommunication;
using Shared.Global;
using Shared.Infrastructure.Communication;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Lua;
using Shared.Models.Communication;
using Shared.Models.Log;
using Shared.Models.MES;
using SqlSugar;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;

namespace Shared.Infrastructure.PackMethod
{
    /// <summary>
    /// MES 数据转换与发送工具，负责按数据结构配置生成 JSON、SOAP、拼接文本，并调用配置的 MES 通信方式。
    /// </summary>
    public static class MesDataConvert
    {
        /// <summary>MES 配置文件根目录。</summary>
        static string _LayoutFile = $"{System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase}\\Config\\MES_Config";

        /// <summary>当前转换字段的错误上下文，用于补充转换异常信息。</summary>
        static string _ErrorCode = null;

        /// <summary>按远端地址缓存的 MES TCP 通信对象。</summary>
        static Dictionary<string, CommunicationBase> _MESObj = new Dictionary<string, CommunicationBase>();

        /// <summary>
        /// 按指定数据结构将业务源数据转换为目标文本。
        /// </summary>
        /// <param name="sourceData">待转换的业务源数据。</param>
        /// <param name="dataLayout">目标数据结构配置。</param>
        /// <returns>转换后的 JSON、SOAP 或拼接文本；结构无效时返回空。</returns>
        public static string Convert(MesDataInfoTree sourceData, DataSruct dataLayout)
        {
            if (dataLayout == null || dataLayout.Structure == null || dataLayout.Structure.Count == 0) return null;
            try
            {
                switch (dataLayout.StructureType)
                {
                    case "JSON":
                        return BuildJsonToken(dataLayout.Structure, CreateJsonBuildContext(sourceData))
                            .ToString(JsonFormatting.None);
                    case "JSONREMOVEQUE"://json的key没有引号
                        return BuildJsonToken(dataLayout.Structure, CreateJsonBuildContext(sourceData))
                            .ToString(JsonFormatting.None)
                            .JsonRemoveQuo();
                    case "JOINT":
                        return ItemsToString(sourceData, dataLayout.Structure);
                    case "SOAP":
                        return BuildSoapDocument(dataLayout.Structure[0], CreateSoapBuildContext(sourceData)).ToString();
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"添加{_ErrorCode} 失败 {ex.Message}");
            }
            return null;
        }
        /// <summary>
        /// 按数据结构名称加载配置并转换业务源数据。
        /// </summary>
        /// <param name="sourceData">待转换的业务源数据。</param>
        /// <param name="structName">数据结构配置名称。</param>
        /// <returns>转换后的目标文本。</returns>
        public static string Convert(MesDataInfoTree sourceData, string structName)
        {
            DataSruct dataLayout = JsonHelper.ReadJson<DataSruct>($"{_LayoutFile}\\DataStructure\\{structName}.json");
            if (dataLayout == null || dataLayout.Structure == null || dataLayout.Structure.Count == 0) throw new Exception($"上位机数据结构为空！！！");
            try
            {
                return Convert(sourceData, dataLayout);
            }
            catch (Exception ex)
            {
                throw new Exception($"上位机添加{_ErrorCode}Exp异常：{ex.Message}");
            }
            throw new Exception($"上位机数据转换为空，请检查数据结构");
        }
        /// <summary>
        /// 加载 MES 接口配置并执行接口 Lua 脚本。
        /// </summary>
        /// <param name="apiName">MES 接口名称。</param>
        /// <param name="sourceData">脚本执行时使用的业务源数据。</param>
        /// <returns>接口执行结果。</returns>
        public static MesResult ExecuteApi(string apiName, MesDataInfoTree sourceData = null)
        {
            string apiFilePath = $"{_LayoutFile}\\ApiConfig\\{apiName}.json";
            APIConfig apiConfig = JsonHelper.ReadJson<APIConfig>(apiName);
            MesResult mesResult = new MesResult();
            if (apiConfig == null)
            {
                mesResult.Message = $"Unfulfilled 上位机未找到 【{apiName}】 接口,{apiFilePath}";
                mesResult.State = MesStatus.UnUpLoad;
                goto sendNG;
            }
            if (string.IsNullOrWhiteSpace(apiConfig.Lua))
            {
                apiConfig.Lua = $"return SendMES(\"{apiName}\")";
            }
            LuaManage luaManage = new LuaManage(sourceData);
            var result = luaManage.DoString(apiConfig.Lua);
            if (result[0] is MesResult mesResult1) return mesResult1;
            Global_Event.WriteLog($"执行脚本出错，错误{result[0]}\r\n脚本：{apiConfig.Lua}", apiName);
            mesResult.Message = $"执行脚本出错，错误{result[0]}\r\n脚本：{apiConfig.Lua}";
            mesResult.State = MesStatus.UpLoadNG;
            return mesResult;
        sendNG:
            Global_Event.WriteLog($"系统校验：{mesResult.State}\r\n系统反馈数据：\r\n{mesResult.Message}", apiName);
            return mesResult;
        }
        /// <summary>
        /// 根据接口配置转换并发送 MES 数据，统一处理鉴权、重传和返回值校验。
        /// </summary>
        /// <param name="apiName">MES 接口名称。</param>
        /// <param name="data">待发送数据；为空时根据接口的数据结构配置自动生成。</param>
        /// <param name="sourceData">生成发送数据和替换 URL 参数所需的业务源数据。</param>
        /// <returns>MES 发送及服务器校验结果。</returns>
        public static MesResult SendMES(string apiName, ref string data, MesDataInfoTree sourceData = null)
        {
            string apiFilePath = $"{_LayoutFile}\\ApiConfig\\{apiName}.json";
            APIConfig apiConfig = JsonHelper.ReadJson<APIConfig>(apiFilePath);
            MesSystemConfig mesSystemConfig = JsonHelper.ReadJson<MesSystemConfig>($"{_LayoutFile}\\MesSystemConfig\\MesSystemConfig.json");
            MesResult mesResult = new MesResult();
            if (mesSystemConfig == null)
            {
                mesResult.Message = $"Unfulfilled 未找到【{apiName}】接口，{apiFilePath}";
                mesResult.State = MesStatus.UnUpLoad;
                goto sendNG;
            }
            if (!apiConfig.IsEnabledAPI)
            {
                mesResult.Message = $"Unfulfilled【{apiName}】接口未启用！！！";
                mesResult.State = MesStatus.UnUpLoad;
                goto sendNG;
            }
            if (string.IsNullOrEmpty(data) && apiConfig.DataStructName == null)
            {
                mesResult.Message = $"【{apiName}】接口未选择数据结构！！！";
                mesResult.State = MesStatus.StructNG;
                goto sendNG;
            }
            if (string.IsNullOrEmpty(data))
            {
                try
                {
                    data = Convert(sourceData, apiConfig.DataStructName);
                }
                catch (Exception ex)
                {
                    mesResult.Message = $"【{apiName}】接口数据结构转换失败，错误{ex}";
                    mesResult.State = MesStatus.StructNG;
                    goto sendNG;
                }
            }
            Global_Event.WriteLog($"【{apiName}】转换后数据：\r\n{data}", apiName);
            int sendSta = -1;
            int sendCount = 0;
            double time = 0;
        SendMES:
            sendCount++;
            Global_Event.WriteLog("开始上传MES。。。", apiName);
            switch (apiConfig.SelectMESType.ToUpper())
            {
                case "WEBSERVICE":
                    XMLConfig xmlConfig = new XMLConfig();
                    xmlConfig.UserName = apiConfig.UserName;
                    xmlConfig.Password = apiConfig.Password;
                    xmlConfig.XMLAction = apiConfig.Action;
                    mesResult.Message = WebServiceHelper.Send(data, apiConfig.Url, xmlConfig, ref sendSta).ToXMLFormat();
                    break;
                case "WEBAPI":
                    string tokenValue = null;
                    if (!string.IsNullOrEmpty(apiConfig.TokenUrl))
                    {
                        mesResult.Message = WebApiHelper.Send(null, apiConfig.TokenUrl, ref sendSta, apiConfig?.Heads?.ToDictionary(r => r.Key, r => r.Value.ToString()), "GET", mesSystemConfig.TimeOut);
                        tokenValue = JsonHelper.GetJsonValue(mesResult.Message, apiConfig.TokenName);
                        Global_Event.WriteLog($"Token: {tokenValue}", apiName);
                        apiConfig.Url = apiConfig.Url.Replace(apiConfig.TokenName.ToUpper(), tokenValue);
                    }
                    Dictionary<string, string> headDic = new Dictionary<string, string>();
                    if (apiConfig.Heads != null && apiConfig.Heads.Count() != 0)
                        foreach (var item in apiConfig.Heads)
                        {
                            if (item.Value.ToUpper().Contains(apiConfig.TokenName.ToUpper()))
                                headDic[item.Key] = item.Value.Replace(apiConfig.TokenName.ToUpper(), tokenValue);
                            else if (item.Value.ToUpper().Contains("GUID"))
                                headDic[item.Key] = Guid.NewGuid().ToString();
                            else if (Global_Data.Heads.Keys.Contains(item.Value))
                                headDic[item.Key] = $"{(item.Key.Equals("Authorization") ? "Bearer " : "")}{item.Value.Replace(item.Value, Global_Data.Heads[item.Value].ToString().Replace("Bearer ", ""))}";
                            else headDic[item.Key] = item.Value;
                            Global_Event.WriteLog($"WEBAPI Head:{item.Key} {headDic[item.Key]}", apiName);
                        }
                    DateTime now = DateTime.Now;
                    apiConfig.Url = GetUrl(apiConfig.Url, sourceData);
                    Global_Event.WriteLog($"WEBAPI Url:{apiConfig.Url}", apiName);
                    mesResult.Message = WebApiHelper.Send(data, apiConfig.Url, ref sendSta, headDic, apiConfig.WebApiType, mesSystemConfig.TimeOut * 1000).ToJsonFormat();
                    time = DateTime.Now.Subtract(now).TotalMilliseconds;
                    break;
                case "TCP CLIENT":
                    string ipPort = $"{apiConfig.TCPRemoteIpAddress}:{apiConfig.TCPRemotePort}";
                    if (!_MESObj.Keys.Contains(ipPort))
                    {
                        TcpClientRuntimeConfig tcpConfig = new TcpClientRuntimeConfig("MES", apiConfig.TCPRemoteIpAddress, apiConfig.TCPRemotePort, apiConfig.TCPLocalIpAddress, apiConfig.TCPLocalPort);
                        _MESObj.Add(ipPort, CommunicationFactory.CreateCommunicationProtocol(tcpConfig));
                        _MESObj[ipPort].OnLog -= MESCommuniactionObj_OnLog;
                        _MESObj[ipPort].OnLog += MESCommuniactionObj_OnLog;
                        _MESObj[ipPort].Start();
                    }
                    Thread.Sleep(100);
                    SendReceiveModel send = new SendReceiveModel(data + (apiConfig.IsEnter ? "\r\n" : ""), mesSystemConfig.TimeOut * 1000);
                    if (_MESObj[ipPort] is not ICommunication messageCommunication)
                    {
                        throw new InvalidOperationException("MES TCP communication object does not support message send.");
                    }

                    sendSta = messageCommunication.Send(ref send, !string.IsNullOrWhiteSpace(apiConfig.ResultCheck)) ? 200 : 500;
                    mesResult.Message = send.Result == null ? "" : send.Result.ToString();
                    if (!apiConfig.IsEnabledTCPKeepAlive) _MESObj[ipPort].Close();
                    break;
                case "FTP":
                    if (apiConfig.IsDown)
                    {
                        mesResult.Message = FTPHelper.Download(apiConfig.Url, apiConfig.UserName, apiConfig.Password, apiConfig.DownPath);
                    }
                    else
                    {
                        mesResult.Message = FTPHelper.UploadFile(apiConfig.Url, apiConfig.UserName, apiConfig.Password, data);
                    }
                    sendSta = 200;
                    break;
                default:
                    break;
            }
            if (sendSta == 200)
            {
                mesResult.State = mesResult.Message.ToUpper().Contains($"{(apiConfig.ResultCheck ?? $"@$#@#$@#$#").ToUpper()}") ? MesStatus.ResultOK : MesStatus.ResultNG;
            }
            else if (sendSta == 401)
            {
                mesResult.State = MesStatus.ResultNG;
                if (string.IsNullOrEmpty(mesResult.Message))
                {
                    mesResult.Message = "用户验证已过期";
                }
            }
            else
            {
                mesResult.State = MesStatus.ResultNG;
            }
            Global_Event.WriteLog($"MES服务器校验：{mesResult.State}\r\nMES服务器反馈数据：\r\n{mesResult.Message}", apiName);
            if (mesResult.State > MesStatus.ResultOK)
            {
                if (sendCount <= mesSystemConfig.RetransmissionsNum && mesResult.Message.ToUpper().Contains("操作超时"))
                {
                    Global_Event.WriteLog($"第 {sendCount} 次上传MES超时，等待 {mesSystemConfig.Interval} s后开始重传。。。", apiName);
                    Thread.Sleep(mesSystemConfig.Interval * 1000);
                    goto SendMES;
                }
                mesResult.Message = $"【{apiName}】MES服务器反馈NG，请检查MES服务器具体报错：MES返回消息编码：{sendSta}；MES返回消息内容：{mesResult.Message}";
            }
            return mesResult;
        sendNG:
            Global_Event.WriteLog($"系统校验：{mesResult.State}\r\n系统反馈数据：\r\n{mesResult.Message}", apiName);
            return mesResult;
        }
        #region JsonToModel
        /// <summary>
        /// 从 JSON 文件生成数据结构配置树。
        /// </summary>
        /// <param name="jsonPath">JSON 文件路径。</param>
        /// <returns>解析后的结构树根节点。</returns>
        public static TreeModel DeserializeFromJsonFile(string jsonPath)
        {
            JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(jsonPath));
            TreeModel model = new TreeModel() { ClientCode = Path.GetFileNameWithoutExtension(jsonPath), DataType = "JSON"};
            // JSON 文件解析必须从根节点进入递归构建，否则界面只能得到一个没有子项的空结构。
            AddItemFromJsonNote(jsonNode, ref model);
            return model;
        }
        /// <summary>
        /// 递归读取 JSON 节点，并将对象、数组和标量追加到结构树。
        /// </summary>
        /// <param name="jsonNode">当前 JSON 节点。</param>
        /// <param name="tree">当前结构树节点。</param>
        private static void AddItemFromJsonNote(JsonNode jsonNode, ref TreeModel tree)
        {
            if (jsonNode == null) return;
            if (jsonNode is JsonObject jsonObj)
            {
                foreach (var kvp in jsonObj)
                {
                    if (kvp.Value is JsonObject)
                    {
                        TreeModel treeModel = new TreeModel()
                        {
                            MESCode = kvp.Key,
                            DataType = "Json"
                        };
                        tree.Children.Add(treeModel);
                        JsonNode value = kvp.Value;
                        AddItemFromJsonNote(value, ref treeModel);
                    }
                    else if (kvp.Value is JsonArray jsonArr)
                    {
                        TreeModel treeModel = new TreeModel() { MESCode = kvp.Key, DataType = "List" };
                        tree.Children.Add(treeModel);
                        for (int i = 0; i < jsonArr.Count; i++)
                        {
                            JsonNode item = jsonArr[i];
                            TreeModel treeModel1 = new TreeModel() { MESCode = kvp.Key, DataType = "Model" };
                            treeModel.Children.Add(treeModel1);
                            AddItemFromJsonNote(item, ref treeModel1);
                        }
                    }
                    else if (kvp.Value is JsonValue)
                    {
                        tree.Children.Add(new TreeModel { MESCode = kvp.Key, DataType = GetJsonObjectType(kvp), ClientCode = kvp.Value.ToString() });
                    }
                }
            }
            else if (jsonNode is JsonArray jsonArr)
            {
                tree.DataType = "List";
                for (int i = 0; i < jsonArr.Count; i++)
                {
                    JsonNode item = jsonArr[i];
                    TreeModel itemModel = new TreeModel
                    {
                        MESCode = tree.MESCode ?? tree.ClientCode,
                        DataType = "Model"
                    };
                    tree.Children.Add(itemModel);
                    AddItemFromJsonNote(item, ref itemModel);
                }
            }
            else if (jsonNode is JsonValue jsonValue)
            {
                KeyValuePair<string, JsonNode> valueEntry = new("Value", jsonValue);
                tree.Children.Add(new TreeModel
                {
                    MESCode = "Value",
                    DataType = GetJsonObjectType(valueEntry),
                    ClientCode = jsonValue.ToString()
                });
            }
        }
        /// <summary>
        /// 根据 JSON 标量类型返回对应的数据结构类型名称。
        /// </summary>
        /// <param name="obj">包含字段名称和值的 JSON 项。</param>
        /// <returns>Double、Bool 或 String。</returns>
        private static string GetJsonObjectType(KeyValuePair<string, JsonNode> obj)
        {
            var kind = obj.Value.GetValueKind();
            string valuetype = string.Empty;
            switch (kind)
            {
                case System.Text.Json.JsonValueKind.Number:
                    valuetype = "Double";
                    break;
                case System.Text.Json.JsonValueKind.True:
                case System.Text.Json.JsonValueKind.False:
                    valuetype = "Bool";
                    break;
                default:
                    valuetype = "String";
                    break;
            }
            return valuetype;
        }
        #endregion
        #region XMLToModel
        /// <summary>
        /// 从 XML 文件生成 SOAP 数据结构配置树。
        /// </summary>
        /// <param name="xmlPath">XML 文件路径。</param>
        /// <returns>解析后的结构树根节点。</returns>
        public static TreeModel DeserializeFromXMLFile(string xmlPath)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(File.ReadAllText(xmlPath));
            TreeModel model = new TreeModel() { ClientCode = Path.GetFileNameWithoutExtension(xmlPath), DataType = "SOAP"};
            AddItemFromXmlNote(xmlDoc.FirstChild, ref model);
            return model;
        }
        /// <summary>
        /// 递归读取 XML 节点、命名空间和子节点，并追加到结构树。
        /// </summary>
        /// <param name="xmlNode">当前 XML 节点。</param>
        /// <param name="tree">当前结构树节点。</param>
        private static void AddItemFromXmlNote(XmlNode xmlNode, ref TreeModel tree)
        {
            if (xmlNode == null) return;
            bool isEmpty = false;
            if (xmlNode.GetType().GetMethod("get_IsEmpty") != null)
                isEmpty = System.Convert.ToBoolean(xmlNode.GetType().GetMethod("get_IsEmpty").Invoke(xmlNode, null));
            if (xmlNode?.Attributes == null) return;
            TreeModel treeModel = new TreeModel() { MESCode = xmlNode.LocalName, DataType = isEmpty ? "XMLNULL" : "String", XMLNameSpace = xmlNode.NamespaceURI };
            foreach (XmlAttribute attribute in xmlNode?.Attributes)
            {
                treeModel.Children.Add(new TreeModel() { MESCode = attribute.LocalName, DataType = "XMLNamespac", XMLNameSpace = attribute.InnerText });
            }
            if (xmlNode?.ChildNodes.Count > 0)
            {
                foreach (XmlNode childNode in xmlNode.ChildNodes)
                {
                    AddItemFromXmlNote(childNode, ref treeModel);
                }
            }
            tree.Children.Add(treeModel);
        }
        #endregion
        /// <summary>
        /// 使用源数据替换 URL 中的单个花括号参数。
        /// </summary>
        /// <param name="url">接口 URL 模板。</param>
        /// <param name="sourceData">参数值来源。</param>
        /// <returns>替换参数后的 URL。</returns>
        private static string GetUrl(string url, MesDataInfoTree sourceData)
        {
            string result = url;
            if (url.Contains("{"))
            {
                int startIndex = url.IndexOf("{") + 1;
                int length = url.IndexOf("}") - startIndex;
                string clientName = url.Substring(startIndex, length);
                string value = sourceData.MesDataInfoItems.FirstOrDefault(r => r.Code == clientName).Value.ToString();
                result = url.Remove(startIndex, length).Insert(startIndex, value).Replace("{", "").Replace("}", "");
            }
            return result;
        }
        /// <summary>
        /// 将 MES TCP 通信日志转发到全局日志入口。
        /// </summary>
        /// <param name="obj">通信日志消息。</param>
        private static void MESCommuniactionObj_OnLog(LogMessageModel obj)
        {
            Global_Event.WriteLog(obj.Message, null);
        }
        #region JSON
        /// <summary>
        /// 为单次 JSON 转换建立源字段索引。索引属于本次调用，避免静态状态导致并发请求互相读取数据。
        /// 同名字段保持旧实现 FirstOrDefault 的行为，只记录首次出现的值。
        /// </summary>
        private sealed class JsonBuildContext
        {
            /// <summary>
            /// 创建单次 JSON 构建上下文，并共享引用布局缓存和循环引用检测集合。
            /// </summary>
            public JsonBuildContext(MesDataInfoTree sourceData, IReadOnlyDictionary<string, object> sourceValues, int? whileIndex = null,
                Dictionary<string, TreeModel> referencedLayouts = null, HashSet<string> activeReferencedLayouts = null)
            {
                SourceData = sourceData;
                SourceValues = sourceValues;
                WhileIndex = whileIndex;
                ReferencedLayouts = referencedLayouts ?? new Dictionary<string, TreeModel>(StringComparer.OrdinalIgnoreCase);
                ActiveReferencedLayouts = activeReferencedLayouts ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>当前转换使用的完整源数据。</summary>
            public MesDataInfoTree SourceData { get; }

            /// <summary>按客户端编码建立的源值索引。</summary>
            public IReadOnlyDictionary<string, object> SourceValues { get; }

            /// <summary>循环结构当前从一开始的序号；非循环节点为空。</summary>
            public int? WhileIndex { get; }

            /// <summary>本次转换已加载的引用布局缓存。</summary>
            public Dictionary<string, TreeModel> ReferencedLayouts { get; }

            /// <summary>当前递归路径中的引用布局，用于检测循环引用。</summary>
            public HashSet<string> ActiveReferencedLayouts { get; }

            /// <summary>
            /// 创建共享当前缓存、但切换循环序号的上下文。
            /// </summary>
            public JsonBuildContext WithWhileIndex(int whileIndex)
            {
                return new JsonBuildContext(SourceData, SourceValues, whileIndex, ReferencedLayouts, ActiveReferencedLayouts);
            }
        }

        /// <summary>
        /// 为单次 JSON 转换建立源值索引和引用布局上下文。
        /// </summary>
        private static JsonBuildContext CreateJsonBuildContext(MesDataInfoTree sourceData)
        {
            Dictionary<string, object> sourceValues = new(StringComparer.Ordinal);
            foreach (MesDataInfoItem sourceItem in sourceData?.MesDataInfoItems ?? Enumerable.Empty<MesDataInfoItem>())
            {
                if (!sourceValues.ContainsKey(sourceItem.Code))
                {
                    sourceValues.Add(sourceItem.Code, sourceItem.Value);
                }
            }
            return new JsonBuildContext(sourceData, sourceValues);
        }

        /// <summary>
        /// 将布局树构建为 JSON 节点。内部始终传递 JToken，只有 Convert 入口负责最终序列化。
        /// </summary>
        private static JToken BuildJsonToken(IEnumerable<TreeModel> layout, JsonBuildContext context)
        {
            IReadOnlyList<TreeModel> items = layout as IReadOnlyList<TreeModel> ?? layout.ToList();
            if (items.Count == 1 && string.IsNullOrEmpty(items[0].MESCode))
            {
                JToken anonymousRoot = BuildJsonNode(items[0], context);
                if (anonymousRoot is JArray) return anonymousRoot;
            }

            JObject result = new();
            foreach (TreeModel item in items)
            {
                AppendJsonNode(result, item, context);
            }
            return result;
        }

        /// <summary>
        /// 将一个布局节点挂载到父对象。普通节点只产生一个属性；普通字段循环会产生多个动态属性，统一在此处分流。
        /// </summary>
        private static void AppendJsonNode(JObject parent, TreeModel node, JsonBuildContext context)
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
        private static JToken BuildJsonNode(TreeModel node, JsonBuildContext context)
        {
            string dataType = node.DataType?.ToUpperInvariant() ?? string.Empty;
            return dataType switch
            {
                "MODEL" when node.IsWhile && context.WhileIndex == null => BuildWhileModels(node, context),
                "MODEL" => BuildJsonModel(node.Children, context),
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
        private static JToken BuildScalarJsonValue(TreeModel node, JsonBuildContext context)
        {
            object value = ResolveSourceValue(context, node);
            if (!node.IsNull && string.IsNullOrEmpty(value?.ToString())) return null;
            return BuildJsonValue(value, node);
        }

        /// <summary>
        /// 构建外部 JSON 布局，并保持原有“作为 JSON 字符串写入当前字段”的配置语义。
        /// </summary>
        private static JToken BuildReferencedJson(TreeModel node, JsonBuildContext context)
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
        private static JObject BuildWhileFields(TreeModel node, JsonBuildContext context)
        {
            JObject fields = new();
            string mesCode = node.MESCode;
            for (int i = 1; i <= node.WhileCount; i++)
            {
                JsonBuildContext whileContext = context.WithWhileIndex(i);
                object value = ResolveSourceValue(whileContext, node);
                if (!node.IsNull && string.IsNullOrEmpty(value?.ToString())) continue;

                node.MESCode = GetWhileName(i, mesCode);
                fields.Add(node.MESCode, BuildJsonValue(value, node));
            }
            return fields;
        }

        /// <summary>
        /// 将普通字段转换为 JSON 值节点。该方法不再感知父对象，也不负责属性挂载。
        /// </summary>
        private static JToken BuildJsonValue(object value, TreeModel layout)
        {
            _ErrorCode = $"ClientName:{layout.ClientCode} MesName:{layout.MESCode} Value:{value}";
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
                    return new JValue(System.Convert.ToDateTime(value).ToString(layout.DefectValue));
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
        private static JArray BuildJsonArray(List<TreeModel> layout, JsonBuildContext context)
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
        private static bool TryAppendDelimitedArrayValues(JArray result, TreeModel item, JsonBuildContext context)
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
                result.Add(BuildJsonValue(value, item));
                return true;
            }

            foreach (string part in text.Split(separator)) result.Add(part);
            return true;
        }

        /// <summary>
        /// 将列表布局的子节点依次转换为 JSON 数组元素。
        /// </summary>
        private static JArray BuildJsonList(List<TreeModel> layout, JsonBuildContext context)
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
        private static JArray BuildWhileModels(TreeModel layout, JsonBuildContext context)
        {
            JArray models = new();
            for (int i = 1; i <= layout.WhileCount; i++)
            {
                JsonBuildContext whileContext = context.WithWhileIndex(i);
                JObject model = BuildJsonModel(layout.Children, whileContext);
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
        private static JObject BuildJsonModel(List<TreeModel> layout, JsonBuildContext context)
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
        private static JArray BuildStepModels(TreeModel layout, JsonBuildContext context)
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
                    model.Add(field.MESCode, BuildJsonValue(value, field));
                }
                models.Add(model);
            }
            return models;
        }

        /// <summary>
        /// 按 ClientCode 从单次转换索引取值，并保持原有的空值回退和循环默认值规则。
        /// </summary>
        private static object ResolveSourceValue(JsonBuildContext context, TreeModel node, string clientCode = null)
        {
            string resolvedClientCode = clientCode ?? node.ClientCode ?? string.Empty;
            if (clientCode == null && context.WhileIndex.HasValue)
            {
                resolvedClientCode = GetWhileName(context.WhileIndex.Value, resolvedClientCode);
            }
            context.SourceValues.TryGetValue(resolvedClientCode, out object value);
            if (!string.IsNullOrEmpty(value?.ToString())) return value;
            if (context.WhileIndex.HasValue && !string.IsNullOrEmpty(node.DefectValue) && node.DefectValue.Contains("["))
            {
                return GetWhileName(context.WhileIndex.Value, node.DefectValue);
            }
            return node.DefectValue;
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
        private sealed class SoapBuildContext
        {
            /// <summary>
            /// 创建单次 SOAP 构建上下文，并共享引用布局缓存和循环引用检测集合。
            /// </summary>
            public SoapBuildContext(MesDataInfoTree sourceData, IReadOnlyDictionary<string, object> sourceValues, XNamespace defaultNamespace,
                int? whileIndex = null, Dictionary<string, TreeModel> referencedLayouts = null, HashSet<string> activeReferencedLayouts = null)
            {
                SourceData = sourceData;
                SourceValues = sourceValues;
                DefaultNamespace = defaultNamespace;
                WhileIndex = whileIndex;
                ReferencedLayouts = referencedLayouts ?? new Dictionary<string, TreeModel>(StringComparer.OrdinalIgnoreCase);
                ActiveReferencedLayouts = activeReferencedLayouts ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>当前转换使用的完整源数据。</summary>
            public MesDataInfoTree SourceData { get; }

            /// <summary>按客户端编码建立的源值索引。</summary>
            public IReadOnlyDictionary<string, object> SourceValues { get; }

            /// <summary>当前节点继承的默认 XML 命名空间。</summary>
            public XNamespace DefaultNamespace { get; }

            /// <summary>循环结构当前从一开始的序号；非循环节点为空。</summary>
            public int? WhileIndex { get; }

            /// <summary>本次转换已加载的引用布局缓存。</summary>
            public Dictionary<string, TreeModel> ReferencedLayouts { get; }

            /// <summary>当前递归路径中的引用布局，用于检测循环引用。</summary>
            public HashSet<string> ActiveReferencedLayouts { get; }

            /// <summary>
            /// 创建共享当前缓存、但切换默认命名空间的上下文。
            /// </summary>
            public SoapBuildContext WithDefaultNamespace(XNamespace defaultNamespace)
            {
                return new SoapBuildContext(SourceData, SourceValues, defaultNamespace, WhileIndex, ReferencedLayouts, ActiveReferencedLayouts);
            }

            /// <summary>
            /// 创建共享当前缓存、但切换循环序号的上下文。
            /// </summary>
            public SoapBuildContext WithWhileIndex(int whileIndex)
            {
                return new SoapBuildContext(SourceData, SourceValues, DefaultNamespace, whileIndex, ReferencedLayouts, ActiveReferencedLayouts);
            }
        }

        /// <summary>
        /// 为单次 SOAP 转换建立源值索引和命名空间上下文。
        /// </summary>
        private static SoapBuildContext CreateSoapBuildContext(MesDataInfoTree sourceData)
        {
            Dictionary<string, object> sourceValues = new(StringComparer.Ordinal);
            foreach (MesDataInfoItem sourceItem in sourceData?.MesDataInfoItems ?? Enumerable.Empty<MesDataInfoItem>())
            {
                if (!sourceValues.ContainsKey(sourceItem.Code)) sourceValues.Add(sourceItem.Code, sourceItem.Value);
            }
            return new SoapBuildContext(sourceData, sourceValues, XNamespace.None);
        }

        /// <summary>
        /// 构建 SOAP/XML 根节点，后续所有子节点都通过 AppendSoapNode 进入同一结构分派流程。
        /// </summary>
        private static XElement BuildSoapDocument(TreeModel rootLayout, SoapBuildContext context)
        {
            XNamespace rootNamespace = ResolveSoapNamespace(rootLayout, context);
            XElement root = new(rootNamespace + rootLayout.MESCode);
            SoapBuildContext childContext = context.WithDefaultNamespace(rootNamespace);
            foreach (TreeModel child in rootLayout.Children)
            {
                AppendSoapNode(root, child, childContext);
            }
            return root;
        }

        /// <summary>
        /// SOAP 结构的唯一分派点。结构方法只负责自己的 XML 语义，不再重复判断全部 DataType。
        /// </summary>
        private static void AppendSoapNode(XElement parent, TreeModel node, SoapBuildContext context)
        {
            string dataType = node.DataType?.ToUpperInvariant() ?? string.Empty;
            switch (dataType)
            {
                case "MODEL" when node.IsWhile && context.WhileIndex == null:
                    foreach (XElement model in BuildWhileSoapModels(node, context)) parent.Add(model);
                    return;
                case "MODEL":
                    parent.Add(BuildSoapModel(node, context));
                    return;
                case "LIST":
                    parent.Add(BuildSoapList(node, context));
                    return;
                case "XMLNAMESPACE":
                    AddSoapNamespaceDeclaration(parent, node);
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
        /// 构建包含子字段的 SOAP 模型元素，并向下传递模型命名空间。
        /// </summary>
        private static XElement BuildSoapModel(TreeModel model, SoapBuildContext context)
        {
            XNamespace nodeNamespace = ResolveSoapNamespace(model, context);
            XElement element = new(nodeNamespace + model.MESCode);
            SoapBuildContext childContext = context.WithDefaultNamespace(nodeNamespace);
            foreach (TreeModel child in model.Children) AppendSoapNode(element, child, childContext);
            return element;
        }

        /// <summary>
        /// 构建 SOAP 列表容器，并按配置顺序追加列表子节点。
        /// </summary>
        private static XElement BuildSoapList(TreeModel list, SoapBuildContext context)
        {
            XNamespace nodeNamespace = ResolveSoapNamespace(list, context);
            XElement element = new(nodeNamespace + list.MESCode);
            SoapBuildContext childContext = context.WithDefaultNamespace(nodeNamespace);
            foreach (TreeModel child in list.Children) AppendSoapNode(element, child, childContext);
            return element;
        }

        /// <summary>
        /// 按循环次数构建 SOAP 模型，遇到缺失的必填字段时停止继续生成。
        /// </summary>
        private static IEnumerable<XElement> BuildWhileSoapModels(TreeModel model, SoapBuildContext context)
        {
            for (int i = 1; i <= model.WhileCount; i++)
            {
                SoapBuildContext whileContext = context.WithWhileIndex(i);
                bool missingRequiredField = model.Children.Any(child =>
                    child.Children.Count == 0 &&
                    !child.IsNull &&
                    string.IsNullOrEmpty(ResolveSoapValue(child, whileContext)?.ToString()));
                if (missingRequiredField) yield break;
                yield return BuildSoapModel(model, whileContext);
            }
        }

        /// <summary>
        /// 解析并转换标量值，生成带正确命名空间的 SOAP 元素。
        /// </summary>
        private static XElement BuildSoapValueElement(TreeModel node, SoapBuildContext context)
        {
            object value = ResolveSoapValue(node, context);
            if (!node.IsNull && string.IsNullOrEmpty(value?.ToString())) return null;
            XNamespace nodeNamespace = ResolveSoapNamespace(node, context);
            return new XElement(nodeNamespace + node.MESCode, ConvertSoapValue(value, node));
        }

        /// <summary>
        /// 按节点数据类型和判断映射规则转换 SOAP 标量值。
        /// </summary>
        private static object ConvertSoapValue(object value, TreeModel node)
        {
            _ErrorCode = $"ClientName:{node.ClientCode} MESName:{node.MESCode} Value:{value}";
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
                "DATETIME" => System.Convert.ToDateTime(string.IsNullOrEmpty(value?.ToString()) ? DateTime.Now : value).ToString(node.DefectValue),
                _ => value?.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// 加载引用布局、构建对应 JSON 文本并包装为 SOAP 子元素。
        /// </summary>
        private static XElement BuildReferencedSoap(TreeModel node, SoapBuildContext context)
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
                JToken json = BuildJsonToken(referencedLayout.Children, CreateJsonBuildContext(context.SourceData));
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
        private static object ResolveSoapValue(TreeModel node, SoapBuildContext context)
        {
            string clientCode = node.ClientCode ?? string.Empty;
            if (context.WhileIndex.HasValue) clientCode = GetWhileName(context.WhileIndex.Value, clientCode);
            context.SourceValues.TryGetValue(clientCode, out object value);
            if (!string.IsNullOrEmpty(value?.ToString())) return value;
            if (context.WhileIndex.HasValue && !string.IsNullOrEmpty(node.DefectValue) && node.DefectValue.Contains("["))
            {
                return GetWhileName(context.WhileIndex.Value, node.DefectValue);
            }
            return node.DefectValue;
        }

        /// <summary>
        /// 获取节点显式命名空间；未配置时继承上下文默认命名空间。
        /// </summary>
        private static XNamespace ResolveSoapNamespace(TreeModel node, SoapBuildContext context)
        {
            return string.IsNullOrWhiteSpace(node.XMLNameSpace) ? context.DefaultNamespace : node.XMLNameSpace;
        }

        /// <summary>
        /// 向父元素追加配置的 XML 命名空间声明。
        /// </summary>
        private static void AddSoapNamespaceDeclaration(XElement parent, TreeModel node)
        {
            if (string.IsNullOrWhiteSpace(node.MESCode) || string.IsNullOrWhiteSpace(node.XMLNameSpace)) return;
            parent.Add(new XAttribute(XNamespace.Xmlns + node.MESCode, node.XMLNameSpace));
        }
        #endregion
        #region JOINT
        /// <summary>
        /// 按结构配置顺序拼接源数据；普通字段保留字段分隔符，循环字段只在循环值之间插入分隔符。
        /// </summary>
        private static string ItemsToString(MesDataInfoTree sourceData, IEnumerable<TreeModel> layout)
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
                if (item.IsWhile)
                {
                    StringBuilder whileResult = new StringBuilder();
                    bool hasAppendedValue = false;
                    for (int index = 1; index <= item.WhileCount; index++)
                    {
                        string clientCode = (item.ClientCode ?? string.Empty).Replace("_", $"{index}_");
                        sourceValues.TryGetValue(clientCode, out object value);
                        if (string.IsNullOrEmpty(value?.ToString()))
                        {
                            value = item.DefectValue;
                        }

                        if (!item.IsNull && string.IsNullOrEmpty(value?.ToString()))
                        {
                            continue;
                        }

                        if (hasAppendedValue)
                        {
                            whileResult.Append(separator);
                        }
                        whileResult.Append(value?.ToString());
                        hasAppendedValue = true;
                    }

                    result.Append(whileResult);
                }
                else
                {
                    sourceValues.TryGetValue(item.ClientCode ?? string.Empty, out object value);
                    bool isDateTime = (item.DataType ?? string.Empty).Contains("DATETIME", StringComparison.OrdinalIgnoreCase);
                    if (!isDateTime && string.IsNullOrEmpty(value?.ToString()))
                    {
                        value = item.DefectValue;
                    }

                    if (!item.IsNull && string.IsNullOrEmpty(value?.ToString()))
                    {
                        continue;
                    }

                    if (isDateTime)
                    {
                        result.Append(System.Convert.ToDateTime(value).ToString(item.DefectValue));
                        result.Append(separator);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(item.JudgeValue))
                    {
                        value = string.Equals(item.JudgeValue, value?.ToString(), StringComparison.Ordinal)
                            ? item.OKText
                            : item.NGText;
                    }

                    result.Append(value?.ToString());
                    result.Append(separator);
                }
            }
            return result.ToString();
        }
        #endregion
    }
}







