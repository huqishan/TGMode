using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.MES.Services
{
    public static class MESExecutionService
    {
        public static MesResult Execute(MesDataInfoTree sourceData)
        {
            string data = string.Empty;
            MesResult result = MesDataConvert.SendMES(sourceData.ApiName, ref data, sourceData);
            return result;
        }
    }
}
