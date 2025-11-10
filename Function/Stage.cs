using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Function
{
    public class Stage
    {
        public string stageCode { get; set; }
        public string stageName { get; set; }
        public int monsterCount { get; set; }
        public string monsterCode1 { get; set; }
        public string monsterCode2 { get; set; }
        public string monsterCode3 { get; set; }
        public string prerequisiteStage { get; set; }
    }
}
