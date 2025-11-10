using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Function
{
    public class LoginDTO
    {
        /// <summary>
        /// Unity가 보낸 JSON Body를 파싱하기 위한 DTO
        /// </summary>
        public string ID { get; set; }
        public string PW { get; set; }
    }
}
