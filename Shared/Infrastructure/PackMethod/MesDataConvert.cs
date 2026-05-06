using Newtonsoft.Json.Linq;
using Shared.Abstractions;
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
    public static class MesDataConvert
    {
        static string _LayoutFile = $"{System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase}\\Config\\MES_Config";
        static string _ErrorCode = null;
        static Dictionary<string, ICommunication> _MESObj = new Dictionary<string, ICommunication>();
        public static string Convert(MesDataInfoTree sourceData, DataSruct dataLayout)
        {
            if (dataLayout == null || dataLayout.Structure == null || dataLayout.Structure.Count == 0) return null;
            JObject jsonObj = null;
            try
            {
                switch (dataLayout.StructureType)
                {
                    case "JSON":
                        jsonObj = new JObject();
                        return ItemsToJsonString(sourceData, dataLayout.Structure, ref jsonObj).Compress();
                    case "JSONREMOVEQUE"://json的key没有引号
                        jsonObj = new JObject();
                        return ItemsToJsonString(sourceData, dataLayout.Structure, ref jsonObj).JsonRemoveQuo();
                    case "JOINT":
                        return ItemsToString(sourceData, dataLayout.Structure);
                    case "SOAP":
                        XNamespace @namespace = dataLayout.Structure[0].XMLNameSpace;
                        XElement root = null;
                        if (@namespace == null) root = new XElement($"{dataLayout.Structure[0].MESCode}");
                        else root = new XElement(@namespace + $"{dataLayout.Structure[0].MESCode}");
                        return root.ToString();
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
        public static string Convert(MesDataInfoTree sourceData, string structName)
        {
            DataSruct dataLayout = JsonHelper.ReadJson<DataSruct>($"{_LayoutFile}\\DataStructure\\{structName}.json");
            if (dataLayout == null || dataLayout.Structure == null || dataLayout.Structure.Count == 0) throw new Exception($"上位机数据结构为空！！！");
            JObject jsonObj;
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
                        CommuniactionConfigModel tcpConfig = new CommuniactionConfigModel(false, "MES", apiConfig.TCPRemoteIpAddress, apiConfig.TCPRemotePort, apiConfig.TCPLocalIpAddress, apiConfig.TCPLocalPort);
                        _MESObj.Add(ipPort, CommunicationFactory.CreateCommuniactionProtocol(tcpConfig));
                        _MESObj[ipPort].OnLog -= MESCommuniactionObj_OnLog;
                        _MESObj[ipPort].OnLog += MESCommuniactionObj_OnLog;
                        _MESObj[ipPort].Start();
                    }
                    Thread.Sleep(100);
                    ReadWriteModel write = new ReadWriteModel(data + (apiConfig.IsEnter ? "\r\n" : ""), mesSystemConfig.TimeOut * 1000);
                    sendSta = _MESObj[ipPort].Write(ref write, !string.IsNullOrWhiteSpace(apiConfig.ResultCheck)) ? 200 : 500;
                    mesResult.Message = write.Result == null ? "" : write.Result.ToString();
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
        public static TreeModel DeserializeFromJsonFile(string jsonPath)
        {
            JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(jsonPath));
            TreeModel model = new TreeModel() { ClientCode = Path.GetFileNameWithoutExtension(jsonPath), DataType = "JSON"};
            return model;
        }
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
                        string key = kvp.Key;
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
                for (int i = 0; i < jsonArr.Count; i++)
                {
                    JsonNode item = jsonArr[i];
                }
            }
        }
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
        public static TreeModel DeserializeFromXMLFile(string xmlPath)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(File.ReadAllText(xmlPath));
            TreeModel model = new TreeModel() { ClientCode = Path.GetFileNameWithoutExtension(xmlPath), DataType = "SOAP"};
            AddItemFromXmlNote(xmlDoc.FirstChild, ref model);
            return model;
        }
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
        private static void MESCommuniactionObj_OnLog(LogMessageModel obj)
        {
            Global_Event.WriteLog(obj.Message, null);
        }
        #region JSON
        private static string ItemsToJsonString(MesDataInfoTree sourceData, IEnumerable<TreeModel> mesDataInfoItems, ref JObject jsonObj)
        {
            foreach (TreeModel item in mesDataInfoItems)
            {
                var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                if (string.IsNullOrEmpty(v?.ToString())) v = item.DefectValue;
                if (item.Children != null && item.Children.Count > 0)
                {
                    if (item.DataType.ToUpper().Equals("LIST") && mesDataInfoItems.Count() == 1 && string.IsNullOrEmpty(item.MESCode))
                    {
                        return AddList(sourceData, item.Children.ToList(), item.MESCode, ref jsonObj).ToString();
                    }
                    else if (item.DataType.ToUpper().Equals("LIST"))
                    {
                        AddList(sourceData, item.Children.ToList(), item.MESCode, ref jsonObj);
                    }
                    else if (item.DataType.ToUpper().Equals("ARRAY"))
                    {
                        jsonObj.Add(item.MESCode, AddJarray(sourceData, item.Children.ToList(), item.MESCode, ref jsonObj));
                    }
                    else if (item.DataType.ToUpper().Equals("MODEL"))
                    {
                        AddModel(sourceData, item.Children.ToList(), item.MESCode, ref jsonObj);
                    }
                    else if (item.DataType.ToUpper().Equals("STRING"))
                    {
                        JObject jsonObj1 = new JObject();
                        ItemsToJsonString(sourceData, item.Children, ref jsonObj1);
                        jsonObj.Add(item.MESCode, jsonObj1.ToString());
                    }
                    else
                    {
                        JObject jsonObj1 = new JObject();
                        ItemsToJsonString(sourceData, item.Children, ref jsonObj1);
                        jsonObj.Add(item.MESCode, jsonObj1);
                    }
                }
                else if (item.DataType.ToUpper().Equals("JSON"))
                {
                    TreeModel dataLayout = JsonHelper.ReadJson<TreeModel>($"{_LayoutFile}\\MESConvertConfig\\{item.ClientCode}.json");
                    JObject jsonObject = new JObject();
                    jsonObj.Add(item.MESCode, ItemsToJsonString(sourceData, dataLayout.Children, ref jsonObject).Compress().ToString());
                }
                else if (item.IsWhile)
                {
                    string mesCode = item.MESCode;
                    for (int i = 1; i <= item.WhileCount; i++)
                    {
                        string clientName = GetWhileName(i, item.ClientCode);
                        v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == $"{clientName}")?.Value;
                        if (string.IsNullOrEmpty(v?.ToString()))
                        {
                            v = !string.IsNullOrEmpty(item.DefectValue) ? item.DefectValue.Contains("[") ? GetWhileName(i, item.DefectValue) : $"{item.DefectValue}" : item.DefectValue;
                        }
                        if (!item.IsNull && string.IsNullOrEmpty(v?.ToString())) continue;
                        item.MESCode = $"{GetWhileName(i, mesCode)}";
                        ValueTypeConvert(v, item, ref jsonObj);
                    }
                }
                else
                {
                    ValueTypeConvert(v, item, ref jsonObj);
                }
            }
            return jsonObj.ToString();
        }
        private static JArray AddJarray(MesDataInfoTree sourceData, List<TreeModel> layout, string rootName, ref JObject root)
        {
            JArray jArray = new JArray();
            foreach (var item in layout)
            {
                if (item.DataType.ToUpper().Equals("ARRAY"))
                {
                    if (item.IsWhile)
                    {
                        for (int i = 1; i <= item.WhileCount; i++)
                        {
                            JArray jArray1 = new JArray();
                            foreach (var m in item.Children)
                            {
                                var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == GetWhileName(i, m.ClientCode))?.Value;
                                if (string.IsNullOrEmpty(v?.ToString()))
                                {
                                    v = !string.IsNullOrEmpty(m.DefectValue) ? m.DefectValue.Contains("[") ? GetWhileName(i, m.DefectValue) : $"{m.DefectValue}" : m.DefectValue;
                                }
                                jArray1.Add(v);
                            }
                            jArray.Add(jArray1);
                        }
                    }
                    else
                    {
                        jArray.Add(AddJarray(sourceData, item.Children.ToList(), rootName, ref root));
                    }
                }
                else
                {
                    if (item.IsWhile)
                    {
                        for (int i = 1; i <= item.WhileCount; i++)
                        {
                            var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == GetWhileName(i, item.ClientCode))?.Value;
                            if (string.IsNullOrEmpty(v?.ToString()))
                            {
                                v = !string.IsNullOrEmpty(item.DefectValue) ? item.DefectValue.Contains("[") ? GetWhileName(i, item.DefectValue) : $"{item.DefectValue}" : item.DefectValue;
                            }
                            object value = VTypeConvert(v, item);
                            if (!item.IsNull && (value == null || System.Convert.ToInt32(value) == -1)) break;
                            jArray.Add(VTypeConvert(v, item));
                        }
                    }
                    else
                    {
                        int startIndex = item.ClientCode.IndexOf('[') + 1;
                        int length = item.ClientCode.IndexOf("]") - startIndex;
                        var v = item.ClientCode.Contains("[") ? sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode.Remove(startIndex - 1, length + 2))?.Value : sourceData?.MesDataInfoItems.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                        if (string.IsNullOrEmpty(v?.ToString()))
                        {
                            v = item.DefectValue;
                        }
                        if (item.ClientCode.Contains("["))
                        {
                            char separator = System.Convert.ToChar(item.ClientCode.Substring(startIndex, 1));
                            if (!string.IsNullOrEmpty(v.ToString()))
                            {
                                if (v.ToString().Contains(separator))
                                {
                                    string[] arr = v.ToString().Split(separator);
                                    foreach (var a in arr)
                                    {
                                        jArray.Add(a);
                                    }
                                }
                                else
                                {
                                    jArray.Add(v);
                                }
                            }
                            else
                            {
                                jArray.Add("");
                            }
                        }
                        else
                        {
                            jArray.Add(v);
                        }
                    }
                }
            }
            return jArray;
        }
        private static object VTypeConvert(object value, TreeModel model)
        {
            switch (model.DataType.ToUpper())
            {
                case "STRING":
                    return value?.ToString();
                case "INT":
                    int result = 0;
                    return int.TryParse(value?.ToString(), out result) ? result : -1;
                case "DOUBLUE":
                    double resultd = 0;
                    return double.TryParse(value?.ToString(), out resultd) ? Math.Round(resultd, System.Convert.ToInt32(model.KeepDecimalLength)) : -1;
                default:
                    break;
            }
            return value;
        }
        private static JArray AddList(MesDataInfoTree sourceData, List<TreeModel> layout, string rootName, ref JObject root)
        {
            JArray jArray = new JArray();
            foreach (var item in layout)
            {
                JObject jsonObjArr = new JObject();
                if (item.DataType.ToUpper().Equals("LIST"))
                {
                    AddList(sourceData, item.Children.ToList(), item.MESCode, ref jsonObjArr);
                    jArray.Add(jsonObjArr);
                }
                else if (item.DataType.ToUpper().Equals("MODEL"))
                {
                    if (item.IsWhile)
                    {
                        AddWhileModel(sourceData, item, ref jsonObjArr);
                    }
                    else
                    {
                        bool nullIsWhile = false;
                        object v = null;
                        foreach (var c in item.Children)
                        {
                            if (c.DataType.ToUpper().Equals("LIST"))
                            {
                                AddList(sourceData, c.Children.ToList(), c.MESCode, ref jsonObjArr);
                            }
                            else if (c.DataType.ToUpper().Equals("MODEL"))
                            {
                                AddModel(sourceData, c.Children.ToList(), c.MESCode, ref jsonObjArr);
                            }
                            else if (c.IsWhile)
                            {
                                string mesCode = c.MESCode;
                                for (int i = 1; i <= c.WhileCount; i++)
                                {
                                    string clientName = GetWhileName(i, c.ClientCode);
                                    v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == $"{clientName}")?.Value;
                                    if (string.IsNullOrEmpty(v?.ToString()))
                                    {
                                        v = !string.IsNullOrEmpty(c.DefectValue) ? c.DefectValue.Contains("[") ? GetWhileName(i, c.DefectValue) : $"{c.DefectValue}" : $"{c.DefectValue}";
                                    }
                                    if (!c.IsNull && string.IsNullOrEmpty(v?.ToString())) continue;
                                    c.MESCode = $"{GetWhileName(i, mesCode)}";
                                    ValueTypeConvert(v, c, ref jsonObjArr);
                                }
                            }
                            else
                            {
                                v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == c.ClientCode)?.Value;
                                if (string.IsNullOrEmpty(v?.ToString())) v = c.DefectValue;
                                if (!c.IsNull && string.IsNullOrEmpty(v?.ToString())) nullIsWhile = true;
                                if (!nullIsWhile) ValueTypeConvert(v, c, ref jsonObjArr);
                            }
                        }
                        if (!nullIsWhile) jArray.Add(jsonObjArr);
                    }
                }
                else if (item.DataType.ToUpper().Equals("STEPMODEL"))
                {
                    var stepNames = sourceData?.MesDataInfoItems?.Where(r => r.Code.ToUpper().Contains("_StepName"));
                    foreach (var stepName in stepNames)
                    {
                        jsonObjArr = new JObject();
                        foreach (var c in item.Children)
                        {
                            var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == $"{stepName.Code.Replace("_StepName", $"_{c.ClientCode}")}")?.Value;
                            if (string.IsNullOrEmpty(v?.ToString()))
                            {
                                v = c.DefectValue;
                            }
                            ValueTypeConvert(v, c, ref jsonObjArr);
                        }
                        jArray.Add(jsonObjArr);
                    }
                }
                else
                {
                    var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                    if (string.IsNullOrEmpty(v?.ToString())) v = item.DefectValue;
                    ValueTypeConvert(v, item, ref jsonObjArr);
                    jArray.Add(jsonObjArr);
                }
            }
            root.Add(rootName ?? "", jArray);
            return jArray;
        }
        private static void AddWhileModel(MesDataInfoTree sourceData, TreeModel layout, ref JObject root)
        {
            for (int i = 1; i < layout.WhileCount; i++)
            {
                JObject jsonObjArr = new JObject();
                int nullCount = 0;
                bool nullIsWhile = false;
                foreach (var c in layout.Children)
                {
                    string clientName = GetWhileName(i, c.ClientCode);
                    var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == $"{clientName}")?.Value;
                    if (string.IsNullOrEmpty(v?.ToString()))
                    {
                        nullCount++;
                        v = !string.IsNullOrEmpty(c.DefectValue) ? c.DefectValue.Contains("[") ? GetWhileName(i, c.DefectValue) : $"{c.DefectValue}" : $"{c.DefectValue}";
                    }
                    if (!c.IsNull && string.IsNullOrEmpty(v?.ToString())) nullIsWhile = true;
                    ValueTypeConvert(v, c, ref jsonObjArr);
                }
                if (nullIsWhile) break;
                root.Add(jsonObjArr);
            }
        }
        private static void AddModel(MesDataInfoTree sourceData, List<TreeModel> layout, string rootName, ref JObject root)
        {
            JObject rootObj1 = new JObject();
            bool isAdd = false;
            foreach (var item in layout)
            {
                if (item.DataType.ToUpper().Equals("LIST"))
                {
                    AddList(sourceData, item.Children.ToList(), item.MESCode, ref rootObj1);
                }
                else if (item.DataType.ToUpper().Equals("MODEL"))
                {
                    AddModel(sourceData, item.Children.ToList(), item.MESCode, ref rootObj1);
                }
                else
                {
                    if (item.IsWhile)
                    {
                        string mesCode = item.MESCode;
                        for (int i = 1; i <= item.WhileCount; i++)
                        {
                            string clientName = GetWhileName(i, item.ClientCode);
                            var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == $"{clientName}")?.Value;
                            if (string.IsNullOrEmpty(v?.ToString()))
                            {
                                v = item.DefectValue;
                            }
                            if (!item.IsNull && string.IsNullOrEmpty(v?.ToString())) continue;
                            item.MESCode = $"{GetWhileName(i, mesCode)}";
                            ValueTypeConvert(v, item, ref rootObj1);
                        }
                    }
                    else if (item.DataType.ToUpper().Equals("JSON"))
                    {
                        TreeModel dataLayout = JsonHelper.ReadJson<TreeModel>($"{_LayoutFile}\\MESConvertConfig\\{item.ClientCode}.json");
                        JObject jsonObject = new JObject();
                        rootObj1.Add(item.MESCode, ItemsToJsonString(sourceData, dataLayout.Children, ref jsonObject).Compress().ToString());
                    }
                    else
                    {
                        var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                        if (string.IsNullOrEmpty(v?.ToString())) v = item.DefectValue;
                        bool nullIsWhile = false;
                        if (!item.IsNull && string.IsNullOrEmpty(v?.ToString())) nullIsWhile = true;
                        if (!nullIsWhile) ValueTypeConvert(v, item, ref rootObj1);
                    }
                }
            }
            root.Add(rootName, rootObj1);
        }
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
        private static void ValueTypeConvert(object value, TreeModel layout, ref JObject root)
        {
            JObject jsonObj = new JObject();
            _ErrorCode = $"ClientName:{layout.ClientCode} MesName:{layout.MESCode} Value:{value}";
            if (!layout.IsNull && string.IsNullOrEmpty(value?.ToString())) return;
            if (!string.IsNullOrEmpty(layout.JudgeValue))
            {
                value = layout.JudgeValue.Equals(value) ? layout.OKText : layout.NGText;
            }
            switch (layout.DataType.ToUpper())
            {
                case "STRING":
                    root.Add(layout.MESCode, (value ?? "")?.ToString());
                    break;
                case "BOOL":
                    bool result = false;
                    if (!bool.TryParse(value.ToString(), out result)) result = value.ToString().ToUpper() == layout.JudgeValue.ToUpper();
                    root.Add(layout.MESCode, result);
                    break;
                case "DATETIME":
                    if (string.IsNullOrEmpty(value?.ToString())) value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ffff");
                    root.Add(layout.MESCode, System.Convert.ToDateTime(value).ToString(layout.DefectValue));
                    break;
                case "TIMETICKS13":
                    if (string.IsNullOrEmpty(value?.ToString())) value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ffff");
                    DateTime time = System.Convert.ToDateTime(value);
                    DateTime starttime = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1, 0, 0, 0, 0));
                    long tick = (time.Ticks - starttime.Ticks) / 10000;
                    root.Add(layout.MESCode, tick);
                    break;
                case "INT":
                    root.Add(layout.MESCode, System.Convert.ToInt32(value));
                    break;
                case "DOUBLE":
                    root.Add(layout.MESCode, Math.Round(System.Convert.ToDouble(value), System.Convert.ToInt32(layout.KeepDecimalLength)));
                    break;
                case "LIST":
                    break;
                case "MODEL":
                    break;
                case "ARRAY":
                    break;
                default://null
                    root.Add(layout.MESCode, null);
                    break;
            }
        }
        private static void ValueTypeConvert(MesDataInfoTree sourceData, TreeModel layout, ref JObject root)
        {

            foreach (TreeModel item in layout.Children)
            {
                JObject jsonObj = new JObject();
                object value = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                _ErrorCode = $"ClientName:{layout.ClientCode} MesName:{layout.MESCode} Value:{value}";
                if (!layout.IsNull && string.IsNullOrEmpty(value?.ToString())) return;
                if (!string.IsNullOrEmpty(layout.JudgeValue))
                {
                    value = layout.JudgeValue.Equals(value) ? layout.OKText : layout.NGText;
                }
                switch (layout.DataType.ToUpper())
                {
                    case "STRING":
                        root.Add(layout.MESCode, (value ?? "")?.ToString());
                        break;
                    case "BOOL":
                        bool result = false;
                        if (!bool.TryParse(value.ToString(), out result)) result = value.ToString().ToUpper() == layout.JudgeValue.ToUpper();
                        root.Add(layout.MESCode, result);
                        break;
                    case "DATETIME":
                        if (string.IsNullOrEmpty(value?.ToString())) value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ffff");
                        root.Add(layout.MESCode, System.Convert.ToDateTime(value).ToString(layout.DefectValue));
                        break;
                    case "TIMETICKS13":
                        if (string.IsNullOrEmpty(value?.ToString())) value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ffff");
                        DateTime time = System.Convert.ToDateTime(value);
                        DateTime starttime = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1, 0, 0, 0, 0));
                        long tick = (time.Ticks - starttime.Ticks) / 10000;
                        root.Add(layout.MESCode, tick);
                        break;
                    case "INT":
                        root.Add(layout.MESCode, System.Convert.ToInt32(value));
                        break;
                    case "DOUBLE":
                        root.Add(layout.MESCode, Math.Round(System.Convert.ToDouble(value), System.Convert.ToInt32(layout.KeepDecimalLength)));
                        break;
                    case "LIST":
                        JArray jArray = new JArray();
                        ValueTypeConvert(sourceData, item, ref jsonObj);
                        jArray.Add(jsonObj);
                        root.Add(item.MESCode ?? "", jArray);
                        break;
                    case "MODEL":
                        ValueTypeConvert(sourceData, item, ref jsonObj);
                        root.Add(item.MESCode ?? "", jsonObj);
                        break;
                    case "ARRAY":
                        break;
                    default://null
                        root.Add(layout.MESCode, null);
                        break;
                }
            }
        }
        #endregion
        #region SOAP
        private static XElement ItemsToSOAPString(MesDataInfoTree sourceData, XNamespace soapNamespace, IEnumerable<TreeModel> mesDataInfoItems, ref XElement root)
        {
            foreach (var item in mesDataInfoItems)
            {
                XNamespace @namespace = item.XMLNameSpace ?? "";
                var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                if (string.IsNullOrEmpty(v?.ToString())) v = item.DefectValue;
                if (item.Children != null && item.Children.Count > 0)
                {
                    XElement element = new XElement(@namespace + item.MESCode);
                    if (item.DataType.ToUpper().Equals("LIST"))
                    {
                        AddList(sourceData, item.Children.ToList(), soapNamespace, ref element);
                    }
                    else
                    {
                        ItemsToSOAPString(sourceData, soapNamespace, item.Children, ref element);
                    }
                }
                else if (item.DataType.ToUpper().Equals("JSON"))
                {
                    XElement element = new XElement(@namespace + item.MESCode);
                    TreeModel dataLayout = JsonHelper.ReadJson<TreeModel>($"{_LayoutFile}\\MESConvertConfig\\{item.ClientCode}.json");
                }
                else
                {
                    ValueTypeConvert(@namespace, v, item, ref root);
                }
            }
            return root;
        }
        private static void AddList(MesDataInfoTree sourceData, List<TreeModel> layout, XNamespace soapNamespace, ref XElement rootXml)
        {
            foreach (var item in layout)
            {
                XNamespace @namespace = string.IsNullOrEmpty(item.XMLNameSpace) ? soapNamespace : item.XMLNameSpace;
                XElement element = new XElement(@namespace + item.MESCode);
                object v;
                if (item.DataType.ToUpper().Equals("LIST"))
                {
                    AddList(sourceData, item.Children.ToList(), soapNamespace, ref rootXml);
                }
                else if (item.DataType.ToUpper().Equals("MODEL"))
                {
                    if (item.IsWhile)
                    {
                        for (int i = 1; i <= item.WhileCount; i++)
                        {
                            element = new XElement(@namespace + item.MESCode);
                            bool isAdd = true;
                            foreach (var c in item.Children)
                            {
                                string clientName = GetWhileName(i, c.ClientCode);
                                v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == clientName)?.Value;
                                if (string.IsNullOrEmpty(v?.ToString())) v = !string.IsNullOrEmpty(c.DefectValue) ? c.DefectValue.Contains("[") ? GetWhileName(i, c.DefectValue) : $"{c.DefectValue}" : c.DefectValue;
                                if (string.IsNullOrEmpty(v?.ToString()) && !c.IsNull) isAdd = false;
                                ValueTypeConvert(@namespace, v, c, ref element);
                            }
                            if (isAdd) rootXml.Add(element);
                        }
                    }
                    else
                    {
                        foreach (var c in item.Children)
                        {
                            v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == c.ClientCode)?.Value;
                            if (string.IsNullOrEmpty(v?.ToString())) v = c.DefectValue;
                            ValueTypeConvert(@namespace, v, c, ref element);
                        }
                        rootXml.Add(element);
                    }
                }
                else
                {
                    v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                    if (string.IsNullOrEmpty(v?.ToString())) v = item.DefectValue;
                    ValueTypeConvert(@namespace, v, item, ref element);
                    rootXml.Add(element);
                }
            }
        }
        private static void ValueTypeConvert(XNamespace @namespace, object value, TreeModel model, ref XElement rootXml)
        {
            _ErrorCode = $"ClientName:{model.ClientCode} MESName:{model.MESCode} Value:{value}";
            XElement element = new XElement(@namespace + model.MESCode);
            if (!string.IsNullOrEmpty(model.JudgeValue))
            {
                value = model.JudgeValue.ToUpper().Equals(value.ToString().ToUpper()) ? model.OKText : model.NGText;
            }
            switch (model.DataType.ToUpper())
            {
                case "STRING":
                    element.Add((value ?? "").ToString());
                    break;
                case "INT":
                    element.Add(System.Convert.ToInt32(value));
                    break;
                case "DOUBULE":
                    element.Add(Math.Round(System.Convert.ToDouble(value), System.Convert.ToInt32(model.KeepDecimalLength)));
                    break;
                case "XMLNAMESPACE":
                    XAttribute xAttribute = new XAttribute(XNamespace.Xmlns + $"{model.MESCode}", model.XMLNameSpace);
                    rootXml.Add(xAttribute);
                    break;
                case "DATETIME":
                    element.Add(System.Convert.ToInt32(value));
                    if (string.IsNullOrEmpty(value?.ToString())) value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ffff");
                    element.Add(System.Convert.ToDateTime(value).ToString(model.DefectValue));
                    break;
                default:
                    break;
            }
        }
        #endregion
        #region JOINT
        private static string ItemsToString(MesDataInfoTree sourceData, IEnumerable<TreeModel> layout)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TreeModel item in layout)
            {
                if (item.IsWhile)
                {
                    string data = null;
                    for (int i = 1; i <= item.WhileCount; i++)
                    {
                        var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == $"{item.ClientCode.Replace("_", $"{i}_")}")?.Value;
                        if (string.IsNullOrEmpty(v?.ToString()))
                        {
                            v = !string.IsNullOrEmpty(item.DefectValue) ? $"{item.DefectValue}" : item.DefectValue;
                        }
                        if (!item.IsNull && string.IsNullOrEmpty(v?.ToString())) continue;
                        data += $"{v?.ToString()}{item.MESCode}";
                    }
                    if (data != null)
                        sb.Append(data.Substring(0, data.Length - item.MESCode.Length));
                }
                else if (item.DataType.ToUpper().Contains("DATETIME"))
                {
                    var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                    if (!item.IsNull && string.IsNullOrEmpty(v?.ToString())) continue;
                    sb.Append(System.Convert.ToDateTime(v).ToString(item.DefectValue)?.ToString() + item.MESCode);
                }
                else
                {
                    var v = sourceData?.MesDataInfoItems?.FirstOrDefault(r => r.Code == item.ClientCode)?.Value;
                    if (string.IsNullOrEmpty(v?.ToString())) v = item.DefectValue;
                    if (!item.IsNull && string.IsNullOrEmpty(v?.ToString())) continue;
                    if (!string.IsNullOrEmpty(item.JudgeValue))
                    {
                        v = item.JudgeValue.Equals(v) ? item.OKText : item.NGText;
                    }
                    sb.Append(v?.ToString() + item.MESCode);
                }
            }
            return sb.ToString();
        }
        #endregion
    }
}







