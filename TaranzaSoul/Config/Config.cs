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

        [JsonProperty("guild_id")]
        public ulong HomeGuildId { get; set; }

        [JsonProperty("log_channel")]
        public ulong MainChannelId { get; set; }

        [JsonProperty("filtered_channel")]
        public ulong FilteredChannelId { get; set; }

        [JsonProperty("access_role")]
        public ulong AccessRoleId { get; set; }

        [JsonProperty("staff_role")]
        public ulong StaffId { get; set; }

        [JsonProperty("helper_role")]
        public ulong HelperId { get; set; }

        [JsonProperty("staff_role_secondary_mention")]
        public bool AlternateStaffMention { get; set; } = false;

        [JsonProperty("staff_mention_role")]
        public ulong AlternateStaffId { get; set; } = 0;

        [JsonProperty("report_channel")]
        public ulong ReportChannelId { get; set; } = 0;

        [JsonProperty("minimum_age")]
        public int MinimumAccountAge { get; set; } = 14; // Age in days

        [JsonProperty("remove_spoilers")]
        public bool RemoveSpoilers { get; set; } = true;

        [JsonProperty("database_connection")]
        public string DatabaseConnectionString { get; set; } = "";

        [JsonProperty("fc_database")]
        public string FriendCodeDatabaseConnectionString { get; set; } = "";

        [JsonProperty("pinnedmessage")]
        public ulong FCPinnedMessageId { get; set; } = 0;

        [JsonProperty("pinned_message_list")]
        public List<ulong> FCPinnedMessages { get; set; } = new List<ulong>();

        [JsonProperty("owner_ids")]
        public List<ulong> OwnerIds { get; set; } = new List<ulong>();

        [JsonProperty("watched_ids")]
        public Dictionary<ulong, string> WatchedIds { get; set; } = new Dictionary<ulong, string>();

        [JsonProperty("color_abusers")]
        public Dictionary<ulong, DateTimeOffset> BlacklistedUsers { get; set; } = new Dictionary<ulong, DateTimeOffset>();

        // This is super awful and will be replaced before the next time this happens
        [JsonProperty("vote_start")]
        public DateTimeOffset VoteStartTime = DateTimeOffset.MinValue;

        [JsonProperty("user_votes_gigi")]
        public List<ulong> UserVotesGigi = new List<ulong>();

        [JsonProperty("user_votes_leo")]
        public List<ulong> UserVotesLeo = new List<ulong>();

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
