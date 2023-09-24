using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ToolCore.Utils
{
    internal class Utils
    {
        public static T CastHax<T>(T typeRef, object castObj) => (T)castObj;
    }
}
