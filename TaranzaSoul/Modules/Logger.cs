using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Diagnostics;
using NodaTime;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Reflection;
using System.IO;
using RestSharp;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace TaranzaSoul
{
    class Logger
    {
        private DiscordSocketClient client;
        private IServiceProvider services;
        private Config config;

        private Dictionary<ulong, Dictionary<ulong, DateTime>> cooldown = new Dictionary<ulong, Dictionary<ulong, DateTime>>();
        private Dictionary<string, Dictionary<ulong, string>> lastImage = new Dictionary<string, Dictionary<ulong, string>>();
        //private Dictionary<ulong, StoredMessage> MessageLogs = new Dictionary<ulong, StoredMessage>();
        private List<ulong> messagedUsers = new List<ulong>();

        private void Log(Exception ex)
        {
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

            string sourceName = ex.Source?.ToString();

            string text;
            if (ex.Message == null)
            {
                text = exMessage ?? "";
                exMessage = null;
            }
            else
                text = ex.Message;

            StringBuilder builder = new StringBuilder(text.Length + (sourceName?.Length ?? 0) + (exMessage?.Length ?? 0) + 5);
            if (sourceName != null)
            {
                builder.Append('[');
                builder.Append(sourceName);
                builder.Append("] ");
            }
            builder.Append($"[{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}] ");
            for (int i = 0; i < text.Length; i++)
            {
                //Strip control chars
                char c = text[i];
                if (c == '\n' || !char.IsControl(c) || c != (char)8226)
                    builder.Append(c);
            }
            if (exMessage != null)
            {
                builder.Append(": ");
                builder.Append(exMessage);
            }

            text = builder.ToString();

            Console.WriteLine(text);
        }

        public async Task Install(IServiceProvider _services)
        {
            client = _services.GetService<DiscordSocketClient>();
            config = _services.GetService<Config>();
            services = _services;

            client.MessageReceived += DMResponse;
            
            client.UserJoined += Client_UserJoined;
            client.UserLeft += Client_UserLeft;
            
            //Task.Run(async () =>
            //{
            //    while (true)
            //    {
            //        try
            //        {
            //            var temp = new Dictionary<ulong, StoredMessage>(MessageLogs);

            //            foreach (var kv in temp)
            //            {
            //                if (kv.Value.Timestamp.ToUniversalTime() > DateTime.UtcNow.AddSeconds(-120))
            //                    MessageLogs.Remove(kv.Key);
            //            }
            //        }
            //        catch (Exception ex)
            //        {
            //            //_manager.Client.Log.Error("Message Logs", ex);
            //        }

            //        await Task.Delay(20 * 1000);
            //    }
            //});
        }

        private async Task Client_UserLeft(SocketGuildUser user)
        {
            try
            {
                if (user.Guild.Id == 132720341058453504)
                {
                    string message = $":door: " +
                        $"**User Left** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                        $"{user.Username}#{user.Discriminator} ({user.Id})" +
                        ((user.JoinedAt.HasValue) ? $"\nOriginal Join Date `{user.JoinedAt.Value.ToLocalTime().ToString("d")} {user.JoinedAt.Value.ToLocalTime().ToString("T")}`" : "");

                    if (config.WatchedIds.ContainsKey(user.Id))
                    {
                        message = $"{message}\n<@&451057945044582400> That user was flagged! {config.WatchedIds[user.Id]}";
                    }

                    await (client.GetGuild(132720341058453504).GetChannel(267377140859797515) as ISocketMessageChannel)
                        .SendMessageAsync(message);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private async Task Client_UserJoined(SocketGuildUser user)
        {
            try
            {
                if (user.Guild.Id == 132720341058453504)
                {
                    string message = $":wave: " +
                        $"**User Joined** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                        $"{user.Username}#{user.Discriminator} ({user.Id}) ({user.Mention})\n" +
                        $"**Account created** `{user.CreatedAt.ToLocalTime().ToString("d")} {user.CreatedAt.ToLocalTime().ToString("T")}`";

                    if (config.WatchedIds.ContainsKey(user.Id))
                    {
                        message = $"{message}\n<@&451057945044582400> This user has been flagged! {config.WatchedIds[user.Id]}";
                    }

                    await (client.GetGuild(132720341058453504).GetChannel(267377140859797515) as ISocketMessageChannel)
                        .SendMessageAsync(message);
                    
                    if (user.Guild.VerificationLevel < VerificationLevel.Extreme)
                        return;


                    if (user.CreatedAt.Date < DateTimeOffset.Now.AddDays(-14))
                    {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        Task.Run(async () =>
                        {
                            try
                            {
                                await user.SendMessageAsync(
                                    "Welcome to the Partnered /r/Kirby Discord Server!\n" +
                                    "To help ensure the peaceful atmosphere of the server, you'll have to wait about 10 minutes until you can see the rest of the channels, " +
                                    "but until then you can familiarize yourself with <#132720402727174144> and <#361565642027171841>. We hope you enjoy your stay!");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error sending welcome message to {user.Id}!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                            }

                            try
                            {
                                var role = client.GetGuild(132720341058453504).GetRole(346373986604810240);
                                await Task.Delay(1000 * 60 * 10); // wait 10 minutes to be closer to Discord's tier 3 verification level and give us a chance to react

                                await user.AddRoleAsync(role);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error adding role to {user.Id}\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                            }
                        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    }
                    else
                    {
                        await Task.Delay(1000);

                        await (client.GetGuild(132720341058453504).GetChannel(346371601564172298) as ISocketMessageChannel)
                            .SendMessageAsync($"<:marxist_think:305877855366152193> " +
                            $"**User Joined** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                            $"{user.Username}#{user.Discriminator} ({user.Id}) ({user.Mention})\n" +
                            $"**Account created** `{user.CreatedAt.ToLocalTime().ToString("d")} {user.CreatedAt.ToLocalTime().ToString("T")}`");

                        try
                        {
                            await user.SendMessageAsync("Hi, welcome to the /r/Kirby Discord server! If you're seeing this, it means **your account is new**, and as such needs to be verified before you can participate in this server. " +
                                "Toss us a mod mail on /r/Kirby with your Discord username and we'll get you set up as soon as we can https://www.reddit.com/message/compose?to=%2Fr%2FKirby");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending new user message to {user.Id}!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
        
        public async Task DMResponse(SocketMessage pMsg)
        {
            if (!(pMsg is SocketUserMessage message)) return;

            if (pMsg.Author.Id == client.CurrentUser.Id) return;
            if (message.Author.IsBot) return;

            if ((message.Channel as IGuildChannel) == null)
            {
                if (!messagedUsers.Contains(message.Author.Id))
                {
                    await message.Channel.SendMessageAsync("I am a utility bot for /r/Kirby. I have no commands, and am otherwise useless in DMs. If you have any questions, please message an online moderator.");
                    messagedUsers.Add(message.Author.Id);
                }

                return;
            }
        }
    }
}

