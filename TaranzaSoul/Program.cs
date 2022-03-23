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
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        private ConcurrentQueue<RoleAddition> RoleAdditions = new ConcurrentQueue<RoleAddition>();
        private DateTimeOffset lastRole = DateTimeOffset.Now;

        private class RoleAddition
        {
            public ulong userId;
            public ulong roleId;
            public bool remove = false;
        }
        

        private async Task RunAsync()
        {
            socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100,
                GatewayIntents = GatewayIntents.All
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

            var map = new ServiceCollection().AddSingleton(socketClient).AddSingleton(config).AddSingleton(logger).AddSingleton(dbhelper).AddSingleton(restClient).BuildServiceProvider();

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
                socketClient.GuildMemberUpdated += SocketClient_GuildMemberUpdated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Source}\n{ex.Message}\n{ex.StackTrace}");
            }

            // perpetual queue to add/remove color roles
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(3000);

                    try
                    {
                        if (lastRole < DateTimeOffset.Now.AddSeconds(-15))
                            continue;

                        List<RoleAddition> temp = new List<RoleAddition>();
                        RoleAddition tempAction = null;

                        while (RoleAdditions.TryDequeue(out tempAction))
                        {
                            if (config.BlacklistedUsers.ContainsKey(tempAction.userId) && 
                                config.BlacklistedUsers[tempAction.userId] > DateTimeOffset.Now)
                                continue;
                            else
                                temp.Add(tempAction);
                        }

                        if (temp.Count() != temp.Select(x => x.userId).Distinct().Count())
                        {
                            // SOMEONE clicked on more than one role at a time!
                            Dictionary<ulong, int> counter = new Dictionary<ulong, int>();

                            foreach (var u in temp.Select(x => x.userId))
                            {
                                if (!counter.ContainsKey(u))
                                    counter[u] = 0;

                                counter[u]++;
                            }

                            foreach (var u in counter)
                            {
                                if (u.Value > 4)
                                {
                                    try
                                    {
                                        await socketClient.GetUser(u.Key).SendMessageAsync("Hey, slow down! You don't need every color on that list! No more colors for an hour!");
                                    }
                                    catch (Exception ex)
                                    {
                                        // User has their DMs disabled
                                    }

                                    await RemoveAllColors(u.Key);
                                    //config.BlacklistedUsers.Add(u.Key, DateTimeOffset.Now.AddHours(1));
                                    Console.WriteLine($"Blacklisted [{u.Key}] from colors.");

                                    while (temp.Select(x => x.userId).Contains(u.Key))
                                    {
                                        temp.Remove(temp.FirstOrDefault(x => x.userId == u.Key));
                                    }
                                }
                            }
                        }

                        foreach (var ra in temp)
                        {
                            if (ra.remove)
                            {
                                await RemoveAllColors(ra.userId);
                            }
                            else
                            {
                                var user = socketClient.GetGuild(config.HomeGuildId).GetUser(ra.userId) as SocketGuildUser;
                                user.AddRoleAsync(user.Guild.GetRole(ra.roleId));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await Task.Delay(1000);
                        // idk something errored
                    }
                }
            });

            //var avatar = new Image(File.OpenRead(".\\TaranzaSOUL.png"));
            //await client.CurrentUser.ModifyAsync(x => x.Avatar = avatar);

            await Task.Delay(-1);
        }

        private async Task SocketClient_GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> beforeCache, SocketGuildUser after)
        {
            if (!beforeCache.HasValue)
            {
                return;
            }

            SocketGuildUser before = beforeCache.Value;

            if (before.Guild.Id == config.HomeGuildId)
            {
                var role = before.Guild.GetRole(694426304510033970);
                if (!before.Roles.Contains(role) && after.Roles.Contains(role))
                {
                    await (before.Guild.GetChannel(694425958928875560) as SocketTextChannel)
                        .SendMessageAsync($"Hi there, {before.Mention}! Welcome to Kirby's stomach!\nNow don't panic, you're not dead, and there *are* plans to build an amusement park in here, but for now just try to enjoy your time here as best you can. You're in good company.");
                }
                else if (before.Roles.Contains(role) && !after.Roles.Contains(role))
                {
                    await (before.Guild.GetChannel(694425958928875560) as SocketTextChannel)
                        .SendMessageAsync($"{before.Mention} was spat out. We wish them well outside the safety of Kirby's stomach.");
                }
            }
        }

        private async Task RemoveAllColors(ulong user)
        {
            //List<IRole> roles = new List<IRole>();

            try
            {
                var guildUser = socketClient.GetGuild(config.HomeGuildId).GetUser(user);

                foreach (var u in guildUser.Roles)
                {
                    if (RoleColors.Values.Contains(u.Id))
                    {
                        await guildUser.RemoveRoleAsync(u);
                        //await Task.Delay(2000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Source}\n{ex.Message}\n{ex.StackTrace}");
            }

            return;

            //socketClient.GetGuild(config.HomeGuildId).GetUser(user).RemoveRolesAsync(roles);
        }

        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (updateChannel != 0 && guild.GetTextChannel(updateChannel) != null)
            {
                await Task.Delay(3000); // wait 3 seconds just to ensure we can actually send it. this might not do anything.
                await guild.GetTextChannel(updateChannel).SendMessageAsync("Successfully reconnected.");
                updateChannel = 0;
            }

            // scan for people with multiple color roles that aren't mods
            Task.Run(async () =>
            {
                try
                {
                    if (guild.Id != config.HomeGuildId)
                        return;

                    var homeServer = socketClient.GetGuild(config.HomeGuildId);

                    List<SocketGuildUser> multiroledrifters = new List<SocketGuildUser>();
                    var staffRole = homeServer.GetRole(config.StaffId);

                    foreach (var u in homeServer.Users)
                    {
                        if (u.Roles.Contains(staffRole))
                            continue;

                        int i = 0;
                        foreach (var r in u.Roles)
                        {
                            if (RoleColors.Values.Contains(r.Id))
                                i++;

                            if (i > 1)
                            {
                                multiroledrifters.Add(u);
                                break;
                            }
                            else if (config.BlacklistedUsers.ContainsKey(u.Id) && i == 1)
                            {
                                multiroledrifters.Add(u);
                                break;
                            }
                        }
                    }

                    foreach (var idiot in multiroledrifters)
                    {
                        // await RemoveAllColors(idiot.Id);
                        // config.BlacklistedUsers[idiot.Id] = DateTimeOffset.Now.AddDays(7);
                        // Console.WriteLine($"[Login check] Blacklisted {idiot} [{idiot.Id}] from colors.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Source}\n{ex.Message}\n{ex.StackTrace}");
                }
            });

            //var emoteServer = socketClient.GetGuild(212053857306542080);
            //var testChannel = socketClient.GetChannel(610210574952955911) as SocketTextChannel;
            //var abilityPlanet = socketClient.GetChannel(431953417024307210) as SocketTextChannel;

            //if (emoteServer == null || homeServer == null || testChannel == null || abilityPlanet == null)
            //    return;

            //var history = abilityPlanet.GetMessagesAsync();

            //var test = await history.FlattenAsync();

            //foreach (var t in test)
            //{
            //    await t.DeleteAsync();
            //    await Task.Delay(1000);
            //}

            //StringBuilder output = new StringBuilder();
            //var i = 0;
            ////List<IEmote> reactions = new List<IEmote>();
            //IEmote[] reactions = new IEmote[4];

            //await abilityPlanet.SendMessageAsync("Click a button to get a color!");
            //await Task.Delay(1000);

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
            //        var msg = await abilityPlanet.SendMessageAsync(output.ToString());
            //        await Task.Delay(1000);
            //        await msg.AddReactionsAsync(reactions);

            //        i = 0;
            //        reactions = new IEmote[4];
            //        output.Clear();
            //        //output.AppendLine();
            //    }
            //}
            //await Task.Delay(1000);

            //var msg2 = await abilityPlanet.SendMessageAsync("If you want to get notified when others want to play uno, press the reaction to get the UNO role!");
            //await Task.Delay(1000);
            //await msg2.AddReactionAsync(emoteServer.Emotes.FirstOrDefault(x => x.Name == "NoU"));


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

        private async Task Client_ReactionRemoved(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            if (reaction.Channel.Id == 431953417024307210 && RoleColors.ContainsKey(reaction.Emote.Name))
            {
                var user = ((SocketGuildUser)reaction.User);

                if (user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                {
                    lastRole = DateTimeOffset.Now;
                    RoleAdditions.Enqueue(new RoleAddition() { userId = user.Id, remove = true });
                    //await user.RemoveRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
                }
            }
            else if (reaction.Channel.Id == 431953417024307210)
            {
                SocketRole role = null;
                var user = ((SocketGuildUser)reaction.User);
                
                switch (reaction.Emote.Name)
                {
                    case "NoU":
                        role = user.Guild.GetRole(498078860517048331);
                        break;
                    case "pitchpls":
                        role = user.Guild.GetRole(639924839091535920);
                        break;
                    case "friendheartred":
                        role = user.Guild.GetRole(749344457345728593);
                        break;
                    case "friendheartteal":
                        role = user.Guild.GetRole(749344359467581476);
                        break;
                    case "friendheartorange":
                        role = user.Guild.GetRole(749345361855905902);
                        break;
                    case "friendheartgrey":
                        role = user.Guild.GetRole(749344514963144714);
                        break;
                    case "PANTS":
                        role = user.Guild.GetRole(759219920771874877);
                        break;
                    case "⛏️":
                        role = user.Guild.GetRole(948944486602534912);
                        break;
                }

                if (role == null)
                    return;

                if (user.Roles.Contains(role))
                    await user.RemoveRoleAsync(role);
            }
            //else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "NoU")
            //{
            //    var user = ((SocketGuildUser)reaction.User);

            //    if (user.Roles.Contains(user.Guild.GetRole(498078860517048331)))
            //        await user.RemoveRoleAsync(user.Guild.GetRole(498078860517048331));
            //}
            //else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "pitchpls")
            //{
            //    var user = ((SocketGuildUser)reaction.User);

            //    if (user.Roles.Contains(user.Guild.GetRole(639924839091535920)))
            //        await user.RemoveRoleAsync(user.Guild.GetRole(639924839091535920));
            //}
        }

        private async Task Client_ReactionAdded(Cacheable<IUserMessage, ulong> cache, Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
        {
            if (reaction.Channel.Id == 431953417024307210 && RoleColors.ContainsKey(reaction.Emote.Name))
            {
                var user = ((SocketGuildUser)reaction.User);

                //await RemoveAllColors(user.Id);

                if (!user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                {
                    lastRole = DateTimeOffset.Now;
                    RoleAdditions.Enqueue(new RoleAddition() { userId = user.Id, roleId = RoleColors[reaction.Emote.Name] });
                    //await user.AddRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
                }
            }
            //else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "🚫")
            //{
            //    var user = ((SocketGuildUser)reaction.User);

            //    foreach (var r in user.Roles.Where(x => RoleColors.ContainsValue(x.Id)))
            //    {
            //        await user.RemoveRoleAsync(r);
            //    }
            //}
            else if (reaction.Channel.Id == 431953417024307210)
            {
                SocketRole role = null;
                var user = ((SocketGuildUser)reaction.User);

                switch (reaction.Emote.Name)
                {
                    case "NoU":
                        role = user.Guild.GetRole(498078860517048331);
                        break;
                    case "pitchpls":
                        role = user.Guild.GetRole(639924839091535920);
                        break;
                    case "friendheartred":
                        role = user.Guild.GetRole(749344457345728593);
                        break;
                    case "friendheartteal":
                        role = user.Guild.GetRole(749344359467581476);
                        break;
                    case "friendheartorange":
                        role = user.Guild.GetRole(749345361855905902);
                        break;
                    case "friendheartgrey":
                        role = user.Guild.GetRole(749344514963144714);
                        break;
                    case "PANTS":
                        role = user.Guild.GetRole(759219920771874877);
                        break;
                    case "⛏️":
                        role = user.Guild.GetRole(948944486602534912);
                        break;
                }

                if (role == null)
                    return;

                if (!user.Roles.Contains(role))
                    await user.AddRoleAsync(role);
            }
            //else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "NoU")
            //{
            //    var user = ((SocketGuildUser)reaction.User);

            //    if (!user.Roles.Contains(user.Guild.GetRole(498078860517048331)))
            //        await user.AddRoleAsync(user.Guild.GetRole(498078860517048331));
            //}
            //else if (reaction.Channel.Id == 431953417024307210 && reaction.Emote.Name == "pitchpls")
            //{
            //    var user = ((SocketGuildUser)reaction.User);

            //    if (!user.Roles.Contains(user.Guild.GetRole(639924839091535920)))
            //        await user.AddRoleAsync(user.Guild.GetRole(639924839091535920));
            //}
        }

        private async Task Client_MessageReceived(SocketMessage msg)
        {
            //if (msg.Source == MessageSource.System)
            //{
            //    var systemMsg = msg as SocketSystemMessage;
            //    if (systemMsg.Type != MessageType.ChannelPinnedMessage)
            //        return;

                
            //}

            if ((msg.Channel.Id == 195126987558354944 || msg.Channel.Id == 599165344019644426) &&
                (msg.Content.ToLower().StartsWith("https://tenor.com/") || msg.Content.ToLower().StartsWith("https://giphy.com/")))
            {
                await msg.DeleteAsync();

                var sentMessage = await msg.Channel.SendMessageAsync($"{msg.Author.Mention} please don't post reaction gifs in the art channels!");

                await Task.Delay(5000);
                await sentMessage.DeleteAsync();
            }

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

            if (msg.Channel.Id == 952748828858155038 || msg.Channel.Id == 361589776433938432 || msg.Channel.Id == 425752341833187328
                || msg.Channel.Id == 952748828858155038 || msg.Channel.Id == 186342269274554368 || msg.Channel.Id == 529898152547844116
                || msg.Channel.Id == 952748828858155038 || msg.Channel.Id == 447789034131947530 || msg.Channel.Id == 429821654068101120
                || msg.Channel.Id == 508903543856562176 || msg.Channel.Id == 346371452620111872 || msg.Channel.Id == 955554476264161300
                || msg.Channel.Id == 268945818470449162)
                return;

            var tmp = msg.Content.ToLower();

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
                    string send = $"{msg.Author.Mention} that's a potential Forgotten Land spoiler! That belongs in <#268945818470449162>!\nCheck in <#431953417024307210> if you need access.";

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
