using Newtonsoft.Json;

namespace Hd2Planets.Models
{
    public record Environment
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}