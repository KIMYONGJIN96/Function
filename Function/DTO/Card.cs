using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Function.Constant.Constant;

namespace Function.DTO
{
    public class Card
    {
        public string cardCode { get; set; }
        public string cardName { get; set; }
        public CardType cardType;
        public int cost { get; set; }
        public int effectValue { get; set; }
        public string description { get; set; }
    }
}
