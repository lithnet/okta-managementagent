using System;
using Newtonsoft.Json;

namespace Lithnet.Okta.ManagementAgent
{
    [JsonObject("watermark")]
    public class Watermark
    {
        [JsonProperty("id")]
        public string ID { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        public Watermark()
        {
        }

        public Watermark(string tableID, string value, string type)
        {
            this.ID = tableID;
            this.Value = value;
            this.Type = type;
        }

        public override string ToString()
        {
            if (this.Type == "DateTime")
            {
                try
                {
                    long ticks = long.Parse(this.Value);
                    return $"{this.ID}:{new DateTime(ticks):s}";
                }
                catch
                {
                    return $"{this.ID}:unknown";
                }
            }
            else
            {
                return $"{this.ID}:{this.Value}";
            }
        }
    }
}
