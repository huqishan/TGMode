using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.Business.Business
{
    public static class System
    {
        /// <summary>
        /// 十六进制字符串转换为普通字符串
        /// </summary>
        /// <param name="hexString">十六进制字符串</param>
        /// <returns></returns>
        public static string HextoString(string hexString)
        {
            if (string.IsNullOrEmpty(hexString))
                return string.Empty;
            try
            {
                var bytes = new byte[hexString.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
                }
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Handle invalid hex string format
                return string.Empty;
            }
        }

        /// <summary>
        /// 字符串转换为十六进制字符串
        /// </summary>
        /// <param name="input">要转换的字符串</param>
        /// <returns>十六进制表示的字符串</returns>
        public static string StringtoHex(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(input);
            var hexString = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                hexString.AppendFormat("{0:x2}", b);
            }
            return hexString.ToString();
        }

        /// <summary>
        /// 获取当前用户名
        /// </summary>
        /// <returns></returns>
        public static string GetCurrentUserName()
        {
            return Environment.UserName;
        }
    }
}
