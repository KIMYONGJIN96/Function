using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Function.DTO
{
    public class RequestLogin
    {
        /// <summary>
        /// Unity가 보낸 JSON Body를 파싱하기 위한 DTO
        /// </summary>
        public string id { get; set; }
        public string pw { get; set; }
    }
}
