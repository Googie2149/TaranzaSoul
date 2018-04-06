using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace TaranzaSoul
{
    class Program
    {
        static void Main(string[] args) =>
            new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient client;
        private Config config;
        private CommandHandler handler;
        private Logger logger;
        private List<string> SpoilerWords = new List<string>();
        private Dictionary<string, ulong> RoleColors = new Dictionary<string, ulong>();

        private async Task RunAsync()
        {
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                //WebSocketProvider = WS4NetProvider.Instance,
                LogLevel = LogSeverity.Verbose
            });
            client.Log += Log;

            config = Config.Load();
            
            var map = new ServiceCollection().AddSingleton(client).AddSingleton(config).BuildServiceProvider();
            
            await client.LoginAsync(TokenType.Bot, config.Token);
            await client.StartAsync();


            logger = new Logger();
            await logger.Install(map);
            SpoilerWords = JsonStorage.DeserializeObjectFromFile<List<string>>("filter.json");
            RoleColors = JsonStorage.DeserializeObjectFromFile<Dictionary<string, ulong>>("colors.json");
            client.MessageReceived += Client_MessageReceived;

            handler = new CommandHandler();
            await handler.Install(map);

            client.Disconnected += Client_Disconnected;
            client.ReactionAdded += Client_ReactionAdded;
            client.ReactionRemoved += Client_ReactionRemoved;

            //await Task.Delay(3000);

            //var avatar = new Image(File.OpenRead(".\\TaranzaSOUL.png"));
            //await client.CurrentUser.ModifyAsync(x => x.Avatar = avatar);

            await Task.Delay(-1);
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

                foreach (var r in user.Roles.Where(x => RoleColors.ContainsValue(x.Id)))
                {
                    await user.RemoveRoleAsync(r);
                }

                if (!user.Roles.Contains(user.Guild.GetRole(RoleColors[reaction.Emote.Name])))
                    await user.AddRoleAsync(user.Guild.GetRole(RoleColors[reaction.Emote.Name]));
            }
        }

        private async Task Client_Disconnected(Exception arg)
        {
            Console.WriteLine("Disconnected event fired!");
        }

        private async Task Client_MessageReceived(SocketMessage msg)
        {
            if (msg.Author.Id == 267405866162978816) return;

            if (msg.Author.Id == 102528327251656704 && msg.Content.ToLower() == "<@267405866162978816> update filter")
            {
                SpoilerWords = JsonStorage.DeserializeObjectFromFile<List<string>>("filter.json");
                await msg.Channel.SendMessageAsync("Done!");
                return;
            }

            if (msg.Author.Id == 102528327251656704 && msg.Content.ToLower() == "<@267405866162978816> update colors")
            {
                RoleColors = JsonStorage.DeserializeObjectFromFile<Dictionary<string, ulong>>("colors.json");
                await msg.Channel.SendMessageAsync("Done!");
                return;
            }

            if (msg.Channel.Id == 417458111553470474 || msg.Channel.Id == 423578054775013377)
                return;

            if (((IGuildChannel)msg.Channel).GuildId != 132720341058453504)
                return;

            var tmp = msg.Content.ToLower();

            if (msg.Content.ToLower().Split(' ').Any(x => SpoilerWords.Contains(x)))
            {

            }

            foreach (var s in SpoilerWords)
            {
                if (msg.Channel.Id == 268945818470449162 && s == "flamberge")
                    continue;

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
                    string send = $"{msg.Author.Mention} that's a spoiler!\n" +
                        $"That belongs in <#417458111553470474>! If you don't already have access to that channel, type `+giveme spoilers`";

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
