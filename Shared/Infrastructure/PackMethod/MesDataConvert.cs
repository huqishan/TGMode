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

            BuildMESData.BuildContext context = BuildMESData.CreateBuildContext(sourceData);
            try
            {
                switch (dataLayout.StructureType)
                {
                    case "JSON":
                        return BuildMESData.BuildJsonToken(dataLayout.Structure, context)
                            .ToString(JsonFormatting.None);
                    case "JSONREMOVEQUE"://json的key没有引号
                        return BuildMESData.BuildJsonToken(dataLayout.Structure, context)
                            .ToString(JsonFormatting.None)
                            .JsonRemoveQuo();
                    case "JOINT":
                        return BuildMESData.ItemsToString(sourceData, dataLayout.Structure);
                    case "SOAP":
                        return BuildMESData.BuildSoapElement(dataLayout.Structure[0], context).ToString();
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                string errorPath = context?.ErrorPath ?? string.Empty;
                throw new Exception($"添加{errorPath} 失败 {ex.Message}", ex);
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
                throw new Exception($"上位机数据结构转换失败：{ex.Message}", ex);
            }
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
            TreeModel treeModel = new TreeModel() { MESCode = xmlNode.LocalName, DataType = isEmpty ? "NULL" : "String", XMLNamespace = xmlNode.NamespaceURI };
            foreach (XmlAttribute attribute in xmlNode?.Attributes)
            {
                treeModel.Children.Add(new TreeModel() { MESCode = attribute.LocalName, DataType = "XMLNamespac", XMLNamespace = attribute.InnerText });
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
        
        
    }
}







