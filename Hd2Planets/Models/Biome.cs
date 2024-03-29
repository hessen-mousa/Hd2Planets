using Newtonsoft.Json;

namespace Hd2Planets.Models
{
    internal record Biome
    {
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
