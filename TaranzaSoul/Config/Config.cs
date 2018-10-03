using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace TaranzaSoul
{
    public class Config
    {
        [JsonProperty("token")]
        public string Token { get; set; }
        [JsonProperty("prefixes")]
        public IEnumerable<string> PrefixList { get; set; } = new[]
        {
            ";",
            "!",
            "pls "
        };
        [JsonProperty("mention_trigger")]
        public bool TriggerOnMention { get; set; } = true;

        [JsonProperty("success_response")]
        public string SuccessResponse { get; set; } = ":thumbsup:";

        [JsonProperty("owner_ids")]
        public List<ulong> OwnerIds { get; set; } = new List<ulong>();

        public async static Task<Config> Load()
        {
            if (File.Exists("config.json"))
            {
                var json = File.ReadAllText("config.json");
                return JsonConvert.DeserializeObject<Config>(json);
            }
            var config = new Config();
            await config.Save();
            throw new InvalidOperationException("configuration file created; insert token and restart.");
        }

        public async Task Save()
        {
            //var json = JsonConvert.SerializeObject(this);
            //File.WriteAllText("config.json", json);
            await JsonStorage.SerializeObjectToFile(this, "config.json");
        }
    }
}
