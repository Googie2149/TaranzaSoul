﻿using System;
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
using System.Threading;
using Discord.Interactions;

namespace TaranzaSoul
{
    public class Logger
    {
        private DiscordSocketClient client;
        private IServiceProvider services;
        private Config config;

        private List<ulong> messagedUsers = new List<ulong>();
        private bool initialized = false;

        private Dictionary<ulong, ulong> VCTCPair = new Dictionary<ulong, ulong>() { {957411264068468766, 957411530972999731} };

        public Dictionary<ulong, DateTimeOffset> prayCooldown = new Dictionary<ulong, DateTimeOffset>();
        public Dictionary<ulong, DateTimeOffset> FightCooldown = new Dictionary<ulong, DateTimeOffset>();
        public DateTimeOffset LastFightTime = DateTimeOffset.MinValue;

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
            client.GuildAvailable += Client_GuildAvailable;
        }


        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (guild.Id == config.HomeGuildId && !initialized)
            {
                // this is used to ensure we don't add multiples of the handler when the bot has to reconnect briefly.
                // I suppose this means that without a full restart, some users might be missed by the bot.
                // Resumes should cover this actually
                // This needs to be moved elsewhere.
                initialized = true;

                Task.Run(async () =>
                {
                    await guild.DownloadUsersAsync();

                    var role = guild.GetRole(config.AccessRoleId);
                    var manualAccess = guild.GetRole(1058192748139774033);

                    //List<SocketGuildUser> newUsers = new List<SocketGuildUser>();

                    foreach (var user in guild.Users)
                    {
                        if (user.Roles.Contains(manualAccess))
                        {
                            if (!user.Roles.Contains(role))
                            {
                                await user.AddRoleAsync(role);
                                await Task.Delay(1000);
                            }
                            //await user.RemoveRoleAsync(manualAccess);
                        }

                        if (!user.Roles.Contains(role))
                        {
                            // I hate dealing with time!
                            if (user.CreatedAt < new DateTime(2022, 9, 1) &&
                            (DateTimeOffset.UtcNow - user.CreatedAt) > TimeSpan.FromDays(config.MinimumAccountAge) &&
                            user.GetAvatarUrl != null)
                                await user.AddRoleAsync(role);
                        }
                    }

                    Console.WriteLine("Adding handlers");

                    client.UserJoined += Client_UserJoined;
                    client.UserLeft += Client_UserLeft;
                    client.UserVoiceStateUpdated += Client_UserVoiceStateUpdated;
                });
            }
            else if (guild.Id != config.HomeGuildId && guild.Id != 473760817809063936 && guild.Id != 212053857306542080)
            {
                await guild.LeaveAsync(); // seriously this bot is only set up to work with a single server
                // and my testing server because reasons
            }
        }

        private async Task Client_UserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (before.VoiceChannel == null && after.VoiceChannel != null) // User is JOINING a VC
            {
                if (VCTCPair.ContainsKey(after.VoiceChannel.Id))
                {
                    var text = after.VoiceChannel.Guild.GetChannel(VCTCPair[after.VoiceChannel.Id]);
                    await text.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                }
            }
            else if (before.VoiceChannel != null && after.VoiceChannel == null) // User is LEAVING a VC
            {
                if (VCTCPair.ContainsKey(before.VoiceChannel.Id))
                {
                    var text = before.VoiceChannel.Guild.GetChannel(VCTCPair[before.VoiceChannel.Id]);
                    await text.RemovePermissionOverwriteAsync(user);
                }
            }
        }
        private async Task Client_UserLeft(SocketGuild guild, SocketUser userVague)
        {
            try
            {
                if (guild.Id == config.HomeGuildId)
                {
                    SocketGuildUser user = userVague as SocketGuildUser;

                    string message = $":door: " +
                        $"**User Left** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                        $"{user.Username}#{user.Discriminator} ({user.Id})" +
                        ((user.JoinedAt.HasValue) ? $"\nOriginal Join Date `{user.JoinedAt.Value.ToLocalTime().ToString("d")} {user.JoinedAt.Value.ToLocalTime().ToString("T")}`" : "");

                    //if (waitingUsers.ContainsKey(user.Id))
                    //    waitingUsers[user.Id].Cancel();

                    if (config.WatchedIds.ContainsKey(user.Id))
                    {
                        //if (config.AlternateStaffMention)
                        //    message = $"{message}\n<@&{config.AlternateStaffId}> That user was flagged! {config.WatchedIds[user.Id]}";
                        //else
                        //    message = $"{message}\n<@&{config.StaffId}> That user was flagged! {config.WatchedIds[user.Id]}";

                        message = $"{message}\nThat user was flagged! {config.WatchedIds[user.Id]}";
                    }

                    await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
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
                if (user.Guild.Id == config.HomeGuildId)
                {
                    if (user.Id == 763820856442486785)
                    {
                        await Task.Delay(1000);
                        await user.AddRoleAsync(957765545086828616);
                    }

                    bool newAccount = false;

                    string message =
                        $"`{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                        $"{user.Username}#{user.Discriminator} ({user.Id}) ({user.Mention})\n" +
                        $"**Account created** `{user.CreatedAt.ToLocalTime().ToString("d")} {user.CreatedAt.ToLocalTime().ToString("T")}`";

                    if (user.GetAvatarUrl() == null && user.CreatedAt.Month == 5 && (user.CreatedAt.Day > 7 || user.CreatedAt.Day < 16))
                    {
                        message = "<a:PANTS:533084296857255947> **User Auto-Banned** " + message;
                        newAccount = true;
                        await user.BanAsync();
                    }
                    else if (DateTimeOffset.UtcNow - user.CreatedAt > TimeSpan.FromDays(config.MinimumAccountAge) && user.GetAvatarUrl() != null)
                    {
                        message = ":wave: **User Joined** " + message;
                    }
                    else
                    {
                        newAccount = true;
                        message = "<:marxist_think:305877855366152193> **Flagged Account** " + message;
                    }

                    if (config.WatchedIds.ContainsKey(user.Id))
                    {
                        //if (config.AlternateStaffMention)
                        //    message = $"{message}\n<@&{config.AlternateStaffId}> This user has been flagged! {config.WatchedIds[user.Id]}";
                        //else
                        //    message = $"{message}\n<@&{config.StaffId}> This user has been flagged! {config.WatchedIds[user.Id]}";

                        message = $"{message}\nThis user has been flagged! {config.WatchedIds[user.Id]}";
                    }

                    await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
                        .SendMessageAsync(message);

                    var role = client.GetGuild(config.HomeGuildId).GetRole(config.AccessRoleId);

                    if (!newAccount)
                        await user.AddRoleAsync(role);
                    else
                        await (client.GetGuild(config.HomeGuildId).GetChannel(config.FilteredChannelId) as ISocketMessageChannel)
                            .SendMessageAsync(message);

                    //await (client.GetGuild(config.HomeGuildId).GetChannel(186342269274554368) as ISocketMessageChannel)
                    //    .SendMessageAsync($"User {user.Mention}\nAvatar ID: `{user.AvatarId}`\nDisplay Avatar Id: `{user.DisplayAvatarId}`\n" +
                    //    $"Avatar URL: `{user.GetAvatarUrl()}`\nDefault Avatar Url: `{user.GetDefaultAvatarUrl()}`\nDisplay Avatar Url: `{user.GetDisplayAvatarUrl()}`\n" +
                    //    $"Guild Avatar Url: `{user.GetGuildAvatarUrl()}`");
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

            var name = client.GetGuild(config.HomeGuildId)?.Name;

            if (name == null)
                // TODO: Add an error log here, we're not in our home guild anymore.
                return;

            if ((message.Channel as IGuildChannel) == null)
            {
                if (!messagedUsers.Contains(message.Author.Id))
                {
                    if (!message.Content.ToLower().StartsWith("!report"))
                    {
                        await message.Channel.SendMessageAsync($"I am a utility bot for {name}. I have few public commands, and am otherwise useless in DMs.\n" +
                            $"To report something to the moderators, please use the `!report` command here, " +
                            $"and please include any relevant details such as who is involved, what channel the event is taking place in, etc.");
                    }

                    messagedUsers.Add(message.Author.Id);
                }

                return;
            }
        }
    }
}

