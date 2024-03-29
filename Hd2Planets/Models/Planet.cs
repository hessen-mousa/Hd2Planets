using Newtonsoft.Json;

namespace Hd2Planets.Models
{
    internal record Planet
    {
        public int Index { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("sector")]
        public string Sector { get; set; }

        [JsonProperty("biome")]
        public Biome Biome { get; set; }

        [JsonProperty("enviromentals")]
        public Models.Environment[] Environments { get; set; }
    }
}
