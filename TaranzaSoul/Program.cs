using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Discord.Commands;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Newtonsoft.Json;

namespace TaranzaSoul
{
    class Program
    {
        static void Main(string[] args) =>
            new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient socketClient;
        private DiscordRestClient restClient;
        private Config config;
        private CommandHandler handler;
        private Logger logger;
        private DatabaseHelper dbhelper;
        private List<string> SpoilerWords = new List<string>();
        private Dictionary<string, ulong> RoleColors = new Dictionary<string, ulong>();
        private ulong updateChannel = 0;
        private int guilds = 0;

        private async Task RunAsync()
        {
            socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100
            });
            socketClient.Log += Log;

            restClient = new DiscordRestClient(new DiscordRestConfig
            {
                LogLevel = LogSeverity.Verbose
            });
            restClient.Log += Log;

            if (File.Exists("./update"))
            {
                var temp = File.ReadAllText("./update");
                ulong.TryParse(temp, out updateChannel);
                File.Delete("./update");
                Console.WriteLine($"Found an update file! It contained [{temp}] and we got [{updateChannel}] from it!");
            }

            config = await Config.Load();

            dbhelper = new DatabaseHelper();
            logger = new Logger();

            var map = new ServiceCollection().AddSingleton(socketClient).AddSingleton(config).AddSingleton(logger).AddSingleton(dbhelper).BuildServiceProvider();

            await socketClient.LoginAsync(TokenType.Bot, config.Token);
            await socketClient.StartAsync();

            await restClient.LoginAsync(TokenType.Bot, config.Token);

            if (File.Exists("./deadlock"))
            {
                Console.WriteLine("We're recovering from a deadlock.");
                File.Delete("./deadlock");
                foreach (var u in config.OwnerIds)
                {
                    (await restClient.GetUserAsync(u))?
                        .SendMessageAsync($"I recovered from a deadlock.\n`{DateTime.Now.ToShortDateString()}` `{DateTime.Now.ToLongTimeString()}`");
                }
            }

            socketClient.GuildAvailable += Client_GuildAvailable;
            socketClient.Disconnected += SocketClient_Disconnected;

            await dbhelper.Install(map);
            await logger.Install(map);
            SpoilerWords = JsonStorage.DeserializeObjectFromFile<List<string>>("filter.json");
            RoleColors = JsonStorage.DeserializeObjectFromFile<Dictionary<string, ulong>>("colors.json");

            handler = new CommandHandler();
            await handler.Install(map);

            try
            {
                socketClient.MessageReceived += Client_MessageReceived;
                socketClient.ReactionAdded += Client_ReactionAdded;
                socketClient.ReactionRemoved += Client_ReactionRemoved;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Source}\n{ex.Message}\n{ex.StackTrace}");
            }

            //var avatar = new Image(File.OpenRead(".\\TaranzaSOUL.png"));
            //await client.CurrentUser.ModifyAsync(x => x.Avatar = avatar);

            await Task.Delay(-1);
        }

        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (updateChannel != 0 && guild.GetTextChannel(updateChannel) != null)
            {
                await Task.Delay(3000); // wait 3 seconds just to ensure we can actually send it. this might not do anything.
                await guild.GetTextChannel(updateChannel).SendMessageAsync("Successfully reconnected.");
                updateChannel = 0;
            }

            guilds++;

            var emoteServer = socketClient.GetGuild(212053857306542080);
            var homeServer = socketClient.GetGuild(config.HomeGuildId);
            var testChannel = socketClient.GetChannel(610210574952955911) as SocketTextChannel;
            var abilityPlanet = socketClient.GetChannel(431953417024307210) as SocketTextChannel;

            if (emoteServer == null || homeServer == null || testChannel == null || abilityPlanet == null)
                return;

            StringBuilder output = new StringBuilder();
            var i = 0;
            //List<IEmote> reactions = new List<IEmote>();
            IEmote[] reactions = new IEmote[4];

            await testChannel.SendMessageAsync("Click a button to get a color!");

            //foreach (var kv in RoleColors)
            //{
            //    var emote = emoteServer.Emotes.FirstOrDefault(x => x.Name == kv.Key);
            //    var role = homeServer.GetRole(kv.Value);

            //    if (i > 0)
            //        output.Append(" ");

            //    output.Append($"{emote} {role.Mention}");
            //    reactions[i] = emote;

            //    i++;

            //    if (i > 3)
            //    {
            //        var msg = await testChannel.SendMessageAsync(output.ToString());
            //        await msg.AddReactionsAsync(reactions);

            //        i = 0;
            //        reactions = new IEmote[4];
            //        output.Clear();
            //        //output.AppendLine();
            //    }
            //}

            //var msg2 = await testChannel.SendMessageAsync("If you want to get notified when others want to play uno, press the reaction to get the UNO role!");
            //await msg2.AddReactionAsync(emoteServer.Emotes.FirstOrDefault(x => x.Name == "NoU"));

            var history = abilityPlanet.GetMessagesAsync();
            //foreach (var m in )
            //{

            //}

            while (await history.GetEnumerator().MoveNext())
            {
                var m = history.GetEnumerator().Current;
                Console.WriteLine(m.First().Content);
            }

            //var uno = await abilityPlanet.GetMessageAsync(498080747656183808) as SocketUserMessage;
            //Console.WriteLine((uno.Reactions.FirstOrDefault().Key as GuildEmote).Url);

            //Console.WriteLine(output.ToString());
        }

        private async Task SocketClient_Disconnected(Exception ex)
        {
            // If we disconnect, wait 3 minutes and see if we regained the connection.
            // If we did, great, exit out and continue. If not, check again 3 minutes later
            // just to be safe, and restart to exit a deadlock.
            var task = Task.Run(async () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    await Task.Delay(1000 * 60 * 3);

                    if (socketClient.ConnectionState == ConnectionState.Connected)
                        break;
                    else if (i == 1)
                    {
                        File.Create("./deadlock");
                        await config.Save();
                        Environment.Exit((int)ExitCodes.ExitCode.DeadlockEscape);
                    }
                }
            });
        }

        private async Task Client_ReactionRemoved(Cacheable<IUserMessage, ulong> cache, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (reaction.Channel.Id == 431953417024307210 && RoleColors.ContainsKey(reaction.Emote.Name))
            {
                var user = ((SocketGuildUser)reaction.User);

                if (user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                    await user.RemoveRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
            }
        }

        private async Task Client_ReactionAdded(Cacheable<IUserMessage, ulong> cache, ISocketMessageChannel channel, SocketReaction reaction)
        {
            if (reaction.Channel.Id == 431953417024307210 && RoleColors.ContainsKey(reaction.Emote.Name))
            {
                var user = ((SocketGuildUser)reaction.User);

                foreach (var r in user.Roles.Where(x => RoleColors.ContainsValue(x.Id) && x.Id != RoleColors[reaction.Emote.Name]))
                {
                    await user.RemoveRoleAsync(r);
                }

                if (!user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                    await user.AddRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
            }
            else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "🚫")
            {
                var user = ((SocketGuildUser)reaction.User);

                foreach (var r in user.Roles.Where(x => RoleColors.ContainsValue(x.Id)))
                {
                    await user.RemoveRoleAsync(r);
                }
            }
        }

        private async Task Client_MessageReceived(SocketMessage msg)
        {
            if (msg.Author.Id == 102528327251656704 && msg.Content.ToLower() == "<@267405866162978816> update colors")
            {
                RoleColors = JsonStorage.DeserializeObjectFromFile<Dictionary<string, ulong>>("colors.json");
                await msg.Channel.SendMessageAsync("Done!");
                return;
            }

            if (msg.Author.Id == 267405866162978816) return;

            if ((msg.Channel as IGuildChannel) == null)
                return;

            if ((msg.Author as IGuildUser).RoleIds.Contains(config.StaffId) &&
                (msg.Content.ToLower() == "<@!267405866162978816> get filter" || msg.Content.ToLower() == "<@267405866162978816> get filter"))
            {
                await msg.Channel.SendMemoryFile("filter.json", JsonConvert.SerializeObject(SpoilerWords, Formatting.Indented));
            }

            if ((msg.Author as IGuildUser).RoleIds.Contains(config.StaffId) &&
                (msg.Content.ToLower() == "<@!267405866162978816> update filter" || msg.Content.ToLower() == "<@267405866162978816> update filter"))
            {
                string file = "";

                string downloadedWords = "";

                if (msg.Attachments.Count() > 0)
                {
                    if (msg.Attachments.FirstOrDefault().Filename.ToLower().EndsWith(".json"))
                        file = msg.Attachments.FirstOrDefault().Url;
                    else
                    {
                        await msg.Channel.SendMessageAsync("That isn't a .json file!");
                        return;
                    }
                }
                else
                {
                    await msg.Channel.SendMessageAsync("I don't see any attachments!");
                    return;
                }

                await Task.Run(async () =>
                {
                    Console.WriteLine($"setting download url to: {file}");

                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            Console.WriteLine("Downloading...");

                            downloadedWords = client.DownloadString(new Uri(file));
                            Console.WriteLine("Downloaded");
                        }
                    }
                    catch (Exception ex)
                    {
                        await msg.Channel.SendMessageAsync($"There was an error downloading that file:\n{ex.Message}");
                        string exMessage;
                        if (ex != null)
                        {
                            while (ex is AggregateException && ex.InnerException != null)
                                ex = ex.InnerException;
                            exMessage = $"{ex.Message}";
                            if (exMessage != "Reconnect failed: HTTP/1.1 503 Service Unavailable")
                                exMessage += $"\n{ex.StackTrace}";
                        }
                        else
                            exMessage = null;

                        Console.WriteLine(exMessage);

                        return;
                    }
                });

                var tempWords = new List<string>();

                try
                {
                    tempWords = JsonConvert.DeserializeObject<List<string>>(downloadedWords);
                }
                catch (Exception ex)
                {
                    await msg.Channel.SendMessageAsync($"There was an error loading that file:\n{ex.Message}");
                    string exMessage;
                    if (ex != null)
                    {
                        while (ex is AggregateException && ex.InnerException != null)
                            ex = ex.InnerException;
                        exMessage = $"{ex.Message}";
                        if (exMessage != "Reconnect failed: HTTP/1.1 503 Service Unavailable")
                            exMessage += $"\n{ex.StackTrace}";
                    }
                    else
                        exMessage = null;

                    Console.WriteLine(exMessage);

                    return;
                }

                try
                {
                    JsonStorage.SerializeObjectToFile(tempWords, "filter.json");
                }
                catch (Exception ex)
                {
                    await msg.Channel.SendMessageAsync($"There was an error replacing the filter:\n{ex.Message}");
                    string exMessage;
                    if (ex != null)
                    {
                        while (ex is AggregateException && ex.InnerException != null)
                            ex = ex.InnerException;
                        exMessage = $"{ex.Message}";
                        if (exMessage != "Reconnect failed: HTTP/1.1 503 Service Unavailable")
                            exMessage += $"\n{ex.StackTrace}";
                    }
                    else
                        exMessage = null;

                    Console.WriteLine(exMessage);

                    return;
                }

                Console.WriteLine("Filter replaced");

                SpoilerWords = JsonStorage.DeserializeObjectFromFile<List<string>>("filter.json");
                await msg.Channel.SendMessageAsync("Done!");
                return;
            }

            if (msg.Channel.Id == 565065746867027987 || msg.Channel.Id == 361589776433938432 || msg.Channel.Id == 425752341833187328
                || msg.Channel.Id == 465651513512165388 || msg.Channel.Id == 186342269274554368 || msg.Channel.Id == 529898152547844116
                || msg.Channel.Id == 231887645888872448 || msg.Channel.Id == 447789034131947530 || msg.Channel.Id == 429821654068101120)
                return;

            var tmp = msg.Content.ToLower();

            if (msg.Content.ToLower().Split(' ').Any(x => SpoilerWords.Contains(x)))
            {

            }

            foreach (var s in SpoilerWords)
            {
                //if (msg.Channel.Id == 268945818470449162 && s == "flamberge")
                //    continue;

                if (tmp.Contains(s))
                {

                    if (!s.Contains(" "))
                    {
                        bool match = false;

                        foreach (var word in tmp.Split(' '))
                        {
                            if (word == s)
                            {
                                match = true;
                                break;
                            }
                        }

                        if (!match) continue;
                    }

                    await Task.Delay(100);
                    await msg.DeleteAsync();
                    string send = $"{msg.Author.Mention} that's a potential endgame spoiler! That belongs in <#565065746867027987>!";

                    await msg.Channel.SendMessageAsync(send);
                }
            }
        }


        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
