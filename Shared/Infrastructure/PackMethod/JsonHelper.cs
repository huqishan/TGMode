using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shared.Infrastructure.PackMethod
{
    public static class JsonHelper
    {
        public static bool SaveJson<T>(T obj, string filePath)
        {
            if (string.IsNullOrEmpty(Path.GetFileName(filePath)))
            {
                filePath += $"\\{typeof(T).Name}.json";
            }
            string path = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            using (Stream writer = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented));
                writer.Write(data, 0, data.Length);
            }
            return true;
        }
        public static T ReadJson<T>(string filePath)
        {
            if (File.Exists(filePath))
            {
                using (StreamReader read = File.OpenText(filePath))
                {
                    return JsonConvert.DeserializeObject<T>(read.ReadToEnd());
                }
            }
            return default(T);
        }
        public static T? DeserializeObject<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            return JsonConvert.DeserializeObject<T>(json);
        }
        public static string SerializeObject<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        #region 获取json中字段值
        /// <summary>
        /// 获取json中字段值
        /// </summary>
        /// <param name="json"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetJsonValue(string json, string key)
        {
            if (!json.StartsWith("{")) return null;
            string result;
            JObject jsonObj = JObject.Parse(json);
            foreach (var item in jsonObj.Children())
            {
                if (item.ToString().Contains(key))
                {
                    JToken jc = (JToken)item;
                    if (((JProperty)jc).Name == key)
                    {
                        return ((JProperty)jc).Value.ToString();
                    }
                    else
                    {
                        if (((JProperty)jc).Value is JArray jArray)
                        {
                            foreach (var arrItem in jArray)
                            {
                                result = GetJsonValue(arrItem.ToString(), key);
                                if (!string.IsNullOrEmpty(result)) return result;
                            }
                        }
                        else
                        {
                            return GetJsonValue(((JProperty)jc).Value.ToString(), key);
                        }
                    }
                }
            }
            return null;
        }
        #endregion

        #region 去除json key双引号
        /// <summary>
        /// 去除json key双引号
        /// </summary>
        /// <param name="jsonInput">json</param>
        /// <returns>去除key引号</returns>
        public static string JsonRemoveQuo(this string jsonInput)
        {
            string result = string.Empty;
            string pattern = "\"(\\w+)\"(\\s*:\\s*)";
            string replacement = "$1$2";
            System.Text.RegularExpressions.Regex rgx = new System.Text.RegularExpressions.Regex(pattern);
            result = rgx.Replace(jsonInput, replacement);
            return result;
        }
        #endregion

        #region Json数据格式化
        public static string ToJsonFormat(this string json)
        {
            try
            {
                JsonSerializer serializer = new JsonSerializer();
                TextReader reader = new StringReader(json);
                JsonTextReader jtr = new JsonTextReader(reader);
                StringWriter stringWriter = new StringWriter();
                object obj = serializer.Deserialize(jtr);
                if (obj != null)
                {
                    JsonTextWriter jsonWriter = new JsonTextWriter(stringWriter)
                    {
                        Formatting = Newtonsoft.Json.Formatting.Indented,
                        Indentation = 4,//缩进字符数
                        IndentChar = ' '//缩进字符
                    };
                    serializer.Serialize(jsonWriter, obj);
                }
                return stringWriter.ToString();
            }
            catch (Exception)
            {
                return json;
            }

        }
        #endregion

        #region 压缩Json字符串
        /// <summary>
        /// 压缩Json字符串
        /// </summary>
        /// <param name="json">需要压缩的json串</param>
        /// <returns></returns>
        public static string Compress(this string json)
        {
            StringBuilder sb = new StringBuilder();
            using (StringReader reader = new StringReader(json))
            {
                int ch = -1;
                int lastch = -1;
                bool isQuoteStart = false;
                while ((ch = reader.Read()) > -1)
                {
                    if ((char)lastch != '\\' && (char)ch == '\"')
                    {
                        if (!isQuoteStart)
                        {
                            isQuoteStart = true;
                        }
                        else
                        {
                            isQuoteStart = false;
                        }
                    }
                    if (!Char.IsWhiteSpace((char)ch) || isQuoteStart)
                    {
                        sb.Append((char)ch);
                    }
                    lastch = ch;
                }
            }
            return sb.ToString();
        }
        #endregion
    }
}
