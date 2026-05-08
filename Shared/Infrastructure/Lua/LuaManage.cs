using Shared.Abstractions.Attributes;
using Shared.Models.MES;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Lua
{
    public class LuaManage
    {
        private NLua.Lua _Lua = null;
        public List<string> LuaMethodNames = new List<string>();
        private MesDataInfoTree _SourceData;
        public LuaManage(MesDataInfoTree sourceData) : base()
        {
            _SourceData = sourceData;
        }
        public LuaManage()
        {
            if (_Lua == null)
            {
                _Lua = new NLua.Lua();
                RegisterFunction();
            }
        }
        public object[] DoString(string lua)
        {
            try
            {
                _Lua.State.Encoding = Encoding.UTF8;
                return _Lua.DoString(lua).Select(ConvertLuaValue).ToArray();
            }
            catch (Exception ex)
            {
                return new object[] { ex };
            }
            finally
            {
                _Lua.Close();
            }
        }

        private static object? ConvertLuaValue(object? value)
        {
            if (value is NLua.LuaTable table)
            {
                Dictionary<string, object?> values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (object key in table.Keys)
                {
                    string? fieldName = Convert.ToString(key);
                    if (!string.IsNullOrWhiteSpace(fieldName))
                    {
                        values[fieldName] = ConvertLuaValue(table[key]);
                    }
                }

                return values;
            }

            return value;
        }

        private void RegisterFunction()
        {
            LuaMethodManage methodManage = new LuaMethodManage();
            typeof(LuaMethodManage).GetMethods().ToList().ForEach(r =>
            {
                var methodName = ((LuaAttribute)r.GetCustomAttributes(typeof(LuaAttribute), false).FirstOrDefault())?.LuaMethodName;
                if (methodName != null)
                {
                    LuaMethodNames.Add(methodName);
                    _Lua.RegisterFunction(methodName, methodManage, r);
                }
            });
        }
    }
}
