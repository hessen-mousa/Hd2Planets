using Newtonsoft.Json;
using System;

namespace Hd2Planets.Models
{
    internal record Campaign
    {
        public Planet Planet { get; set; }

        [JsonProperty("faction")]
        public string Faction { get; set; }

        [JsonProperty("players")]
        public int Players { get; set; }

        [JsonProperty("health")]
        public double Health { get; set; }

        [JsonProperty("maxHealth")]
        public double MaxHealth { get; set; }

        [JsonProperty("percentage")]
        public decimal Percentage { get; set; }

        [JsonProperty("defense")]
        public bool Defense { get; set; }

        [JsonProperty("majorOrder")]
        public bool MajorOrder { get; set; }

        [JsonProperty("expireDateTime")]
        public long ExpireDateTimeEpochFormat { get; set; }

        public DateTime ExpireDateTime
        {
            get
            {
                // Assuming ExpireDateTime is a Unix timestamp in seconds
                // Unix timestamp is seconds past epoch
                DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(ExpireDateTimeEpochFormat);
            }
        }
    }
}
