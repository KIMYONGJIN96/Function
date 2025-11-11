using Function.Constant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Function.Constant.Constant;

namespace Function.DTO
{
    public class Monster
    {
        public string monsterCode { get; set; }
        public string monsterName { get; set; }
        public MonsterGrade grade;
        public int hp { get; set; }
        public int atk { get; set; }
        public int rewardExp { get; set; }
    }
}
