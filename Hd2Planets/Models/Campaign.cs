using Newtonsoft.Json;
using System;

namespace Hd2Planets.Models
{
    internal record Campaign
    {
        public Planet CampaignPlanet { get; set; }

        [JsonProperty("faction")]
        public string Faction { get; set; }

        [JsonProperty("players")]
        public int Players { get; set; }

        [JsonProperty("health")]
        public double Health { get; set; }

        [JsonProperty("maxHealth")]
        public double MaxHealth { get; set; }

        [JsonProperty("percentage")]
        public double Percentage { get; set; }

        [JsonProperty("defense")]
        public bool Defense { get; set; }

        [JsonProperty("majorOrder")]
        public bool MajorOrder { get; set; }

        [JsonProperty("expireDateTime")]
        public long ExpireDateTimeEpochFormat { get; set; }

    }
}
