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
using System.Threading;

namespace TaranzaSoul
{
    public class Logger
    {
        private DiscordSocketClient client;
        private IServiceProvider services;
        private Config config;
        private DatabaseHelper dbhelper;

        private Dictionary<ulong, Dictionary<ulong, DateTime>> cooldown = new Dictionary<ulong, Dictionary<ulong, DateTime>>();
        private Dictionary<string, Dictionary<ulong, string>> lastImage = new Dictionary<string, Dictionary<ulong, string>>();
        //private Dictionary<ulong, StoredMessage> MessageLogs = new Dictionary<ulong, StoredMessage>();
        private List<ulong> messagedUsers = new List<ulong>();
        private bool initialized = false;
        private bool existingTable = false;
        //private Dictionary<ulong, CancellationTokenSource> waitingUsers = new Dictionary<ulong, CancellationTokenSource>();
        private Dictionary<ulong, bool> newUsers = new Dictionary<ulong, bool>();

        private const string WelcomeMessage = 
                                "Welcome to the Partnered /r/Kirby Discord Server!\n" +
                                "To help ensure the peaceful atmosphere of the server, you'll have to wait about 10 minutes until you can see the rest of the channels, " +
                                "but until then you can familiarize yourself with <#132720402727174144> and <#361565642027171841>. We hope you enjoy your stay!";

        private const string WelcomeBackMessage = 
                                "Welcome back to the Partnered /r/Kirby Discord Server! We hope you enjoy your stay!";

        private const string OfflineJoinWelcome =
                                "Welcome to the Partnered /r/Kirby Discord Server!\n" +
                                "This bot was offline when you had joined (probably another Discord outage), but you should have access to the rest of the server now.\n" +
                                "If you haven't already, please familiarize yourself with <#132720402727174144> and <#361565642027171841>. We hope you enjoy your stay!";

        private const string NewUserNotice = 
                                "Hi, welcome to the /r/Kirby Discord server! If you're seeing this, it means **your account is new**, and as such needs to be verified before you can participate in this server. " +
                                "Toss us a mod mail on /r/Kirby with your Discord username and we'll get you set up as soon as we can https://www.reddit.com/message/compose?to=%2Fr%2FKirby";

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
            dbhelper = _services.GetService<DatabaseHelper>();
            services = _services;

            client.MessageReceived += DMResponse;
            client.GuildAvailable += Client_GuildAvailable;
            client.UserIsTyping += Client_UserIsTyping;

            existingTable = await dbhelper.InitializeDB();

            Console.WriteLine($"Table initialized (New: {existingTable})");
        }

        public void RegisterNewUser(ulong userId)
        {
            newUsers[userId] = false;
        }

        public void ReportSent(ulong userId)
        {
            if(!messagedUsers.Contains(userId))
                messagedUsers.Add(userId);
        }

        private async Task Client_UserIsTyping(SocketUser user, ISocketMessageChannel channel)
        {
            if (newUsers.Keys.Contains(user.Id) && newUsers[user.Id] == false)
            {
                newUsers[user.Id] = true;
                await channel.SendMessageAsync($"**Welcome to the /r/Kirby Discord server,** {user.Mention}!\n" +
                    "You're getting this message because you just started typing in the server!\n\n" +
                    "**First time here?** Don't forget to read <#132720402727174144>!\n" +
                    "**Not sure what a channel is for?** Look in <#361565642027171841>!\n" +
                    "**Here for Super Kirby Clash?** Head to <#417458111553470474>!\n" +
                    "**Looking for someone to play with?** Ask in #<#619088469339144202>!");
            }
        }

        //private async Task DelayAddRole(ulong u, CancellationToken cancellationToken, double minutes = 10)
        //{
        //    try
        //    {
        //        var role = client.GetGuild(config.HomeGuildId).GetRole(config.AccessRoleId);
        //        //await Task.Delay(1000 * 60 * 10); 

        //        if (minutes > 0)
        //        {
        //            TimeSpan wait = TimeSpan.FromMinutes(minutes);

        //            for (int i = 0; i < 20; i++)
        //            {
        //                await Task.Delay(TimeSpan.FromMinutes(wait.TotalMinutes / 20));
        //                cancellationToken.ThrowIfCancellationRequested();
        //            }

        //            if (client.GetGuild(config.HomeGuildId).GetUser(u) == null)
        //            {
        //                Console.WriteLine($"{u} isn't in the server anymore!");
        //                return;
        //            }
        //        }

        //        await client.GetGuild(config.HomeGuildId).GetUser(u).AddRoleAsync(role, new RequestOptions() { AuditLogReason = "Automatic approval" });
        //        waitingUsers.Remove(u);
        //    }
        //    catch (OperationCanceledException ex)
        //    {
        //        Console.WriteLine($"{u} left the server, ending early.");
        //        waitingUsers.Remove(u);
        //        return;
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error adding role to {u}\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
        //    }
        //}

        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (guild.Id == config.HomeGuildId && !initialized)
            {
                initialized = true;

                Console.WriteLine("Starting db");

                Task.Run(async () =>
                {
                    await guild.DownloadUsersAsync();

                    var loggedUsers = await dbhelper.GetAllusers();
                    var role = guild.GetRole(config.AccessRoleId);

                    List<LoggedUser> newUsers = new List<LoggedUser>();
                    List<LoggedUser> unapprovedUsers = new List<LoggedUser>();

                    Console.WriteLine($"Grabbed lists of users [{loggedUsers.Count()} vs {guild.Users.Count()}]");

                    if (existingTable)
                    {
                        Console.WriteLine("Table exists");

                        foreach (var u in guild.Users)
                        {
                            if (!loggedUsers.ContainsKey(u.Id))
                            {
                                // We haven't seen this user yet, get to them later
                                // Probably due to someone joining while the bot was offline
                                
                                // If the account is past the minimum age, just let them in. Don't bother checking the 10 minute timer.
                                
                                newUsers.Add(new LoggedUser() {
                                    UserId = u.Id,
                                    NewAccount = u.CreatedAt.Date > DateTimeOffset.Now.AddDays(config.MinimumAccountAge * -1),
                                    ApprovedAccess = !(u.CreatedAt.Date > DateTimeOffset.Now.AddDays(config.MinimumAccountAge * -1))
                                });

                                continue;
                            }
                            else
                            {
                                // We have this user in the list, ensure they have the roles they should
                                if (u.Roles.Contains(role))
                                {
                                    // They have access to the server
                                    if (loggedUsers[u.Id].ApprovedAccess)
                                        continue; // and this matches our records
                                    else
                                    {
                                        // and this does not match our records
                                        // revoke access, notify admins that someone has a role that they shouldn't

                                        await u.RemoveRoleAsync(role, new RequestOptions() { AuditLogReason = "Unapproved access." });

                                        string output = "";
                                        
                                        if (config.AlternateStaffMention)
                                            output = $"<@&{config.AlternateStaffId}> {u.Mention} had access to the server when they shouldn't. Check audit log and see who gave them the role.";
                                        else
                                            output = $"<@&{config.StaffId}> {u.Mention} had access to the server when they shouldn't. Check audit log and see who gave them the role.";

                                        await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
                                            .SendMessageAsync(output);
                                    }
                                }
                                else
                                {
                                    // They don't have the role
                                    if (loggedUsers[u.Id].ApprovedAccess)
                                    {
                                        // they don't have the role when they should
                                        // send a post to admins so this can be dealt with manualy
                                        
                                        string output = "";

                                        if (config.AlternateStaffMention)
                                            output = $"<@&{config.AlternateStaffId}> {u.Mention} needs access.";
                                        else
                                            output = $"<@&{config.StaffId}> {u.Mention} needs access.";

                                        if (loggedUsers[u.Id].ApprovalModId != 0)
                                        {
                                            output = $"{output}\nThey've previously been approved by <@{loggedUsers[u.Id].ApprovalModId}> with the reason `{loggedUsers[u.Id].ApprovalReason}`";
                                        }

                                        await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
                                            .SendMessageAsync(output);
                                    }
                                    else
                                        continue; // and this matches our records
                                }
                            }
                        }

                        Console.WriteLine("Alright, checked through the user lists");

                        //if (newUsers.Count() > 1)
                        //{
                        //    Console.WriteLine($"aaaaaaaaaAAAAAAAAAAAAAAAAAAAAA {newUsers.Count()}");
                        //    throw new Exception("EVERYTHING IS BROKEN");
                        //}

                        // Add all the new users to the database
                        await dbhelper.BulkAddLoggedUser(newUsers);

                        foreach (var u in newUsers)
                        {
                            if (u.NewAccount)
                            {
                                try
                                {
                                    await guild.GetUser(u.UserId).SendMessageAsync(NewUserNotice);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error sending new user message to {u.UserId}!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                                }
                            }
                            else
                            {
                                try
                                {
                                    await guild.GetUser(u.UserId).SendMessageAsync(OfflineJoinWelcome);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error sending offline user message to {u.UserId}!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                                }

                                //CancellationTokenSource source = new CancellationTokenSource();
                                //waitingUsers.Add(u.UserId, source);

                                //Task.Run(async () => DelayAddRole(u.UserId, source.Token, minutes: 0), source.Token);

                                try
                                {
                                    //var role = client.GetGuild(config.HomeGuildId).GetRole(config.AccessRoleId);

                                    var user = client.GetGuild(config.HomeGuildId).GetUser(u.UserId);

                                    if (client.GetGuild(config.HomeGuildId).GetUser(user.Id) == null)
                                    {
                                        Console.WriteLine($"{user.Id} isn't in the server anymore!");
                                        return;
                                    }

                                    await user.AddRoleAsync(role);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error adding role to {u.UserId}\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                                }

                                Console.WriteLine($"Apparently adding user {u.UserId}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Creating new table");

                        // Fresh table, add everyone to the table
                        List<LoggedUser> users = new List<LoggedUser>();

                        foreach (var u in guild.Users)
                        {
                            users.Add(new LoggedUser()
                            {
                                UserId = u.Id,
                                NewAccount = u.CreatedAt.Date > DateTimeOffset.Now.AddDays(config.MinimumAccountAge * -1),
                                ApprovedAccess = u.Roles.Contains(role)
                            });
                        }

                        await dbhelper.BulkAddLoggedUser(users);

                        Console.WriteLine("Table created");
                    }

                    Console.WriteLine("Adding handlers");

                    client.UserJoined += Client_UserJoined;
                    client.UserLeft += Client_UserLeft;
                    client.GuildMemberUpdated += Client_GuildMemberUpdated;
                });
            }
            else if (guild.Id != config.HomeGuildId && guild.Id != 473760817809063936 && guild.Id != 212053857306542080)
            {
                await guild.LeaveAsync(); // seriously this bot is only set up to work with a single server
                // and my testing server because reasons
            }
        }

        private async Task Client_GuildMemberUpdated(SocketGuildUser before, SocketGuildUser after)
        {
            if (before.Guild.Id == config.HomeGuildId)
            {
                var role = before.Guild.GetRole(config.AccessRoleId);

                if (!before.Roles.Contains(role) && after.Roles.Contains(role))
                {
                    // Role was added

                    await Task.Delay(500); // add a delay to make sure it wasn't our own bot that did it

                    var loggedUser = await dbhelper.GetLoggedUser(before.Id);

                    if (!loggedUser.ApprovedAccess)
                    {
                        string output = "";

                        if (config.AlternateStaffMention)
                            output = $"<@&{config.AlternateStaffId}> {before.Mention} was given the role when they shouldn't have it.";
                        else
                            output = $"<@&{config.StaffId}> {before.Mention} was given the role when they shouldn't have it.";

                        if (loggedUser.ApprovalModId != 0)
                        {
                            output = $"{output}\nThey've previously been approved by <@{loggedUser.ApprovalModId}> with the reason `{loggedUser.ApprovalReason}`";
                        }

                        await after.RemoveRoleAsync(role);

                        await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
                            .SendMessageAsync(output);
                    }
                }
                else if (before.Roles.Contains(role) && !after.Roles.Contains(role))
                {
                    // Role was removed

                    await Task.Delay(500);

                    var loggedUser = await dbhelper.GetLoggedUser(before.Id);

                    if (loggedUser.ApprovedAccess)
                    {
                        // don't revoke access
                        // await dbhelper.RevokeApproval(before.Id);

                        string output = "";

                        // if (config.AlternateStaffMention)
                        //     output = $"<@&{config.AlternateStaffId}> {before.Mention} has had their access revoked manually.";
                        // else
                        //     output = $"<@&{config.StaffId}> {before.Mention} has had their access revoked manually.";

                        // send notification without a ping
                        output = $"{before.Mention} has had their access revoked manually.";


                        await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
                            .SendMessageAsync(output);
                    }
                }
            }
        }

        private async Task Client_UserLeft(SocketGuildUser user)
        {
            try
            {
                if (user.Guild.Id == config.HomeGuildId)
                {
                    string message = $":door: " +
                        $"**User Left** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                        $"{user.Username}#{user.Discriminator} ({user.Id})" +
                        ((user.JoinedAt.HasValue) ? $"\nOriginal Join Date `{user.JoinedAt.Value.ToLocalTime().ToString("d")} {user.JoinedAt.Value.ToLocalTime().ToString("T")}`" : "");

                    //if (waitingUsers.ContainsKey(user.Id))
                    //    waitingUsers[user.Id].Cancel();

                    if (config.WatchedIds.ContainsKey(user.Id))
                    {
                        if (config.AlternateStaffMention)
                            message = $"{message}\n<@&{config.AlternateStaffId}> That user was flagged! {config.WatchedIds[user.Id]}";
                        else
                            message = $"{message}\n<@&{config.StaffId}> That user was flagged! {config.WatchedIds[user.Id]}";
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
                    string message = $":wave: " +
                        $"**User Joined** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                        $"{user.Username}#{user.Discriminator} ({user.Id}) ({user.Mention})\n" +
                        $"**Account created** `{user.CreatedAt.ToLocalTime().ToString("d")} {user.CreatedAt.ToLocalTime().ToString("T")}`";

                    if (config.WatchedIds.ContainsKey(user.Id))
                    {
                        if (config.AlternateStaffMention)
                            message = $"{message}\n<@&{config.AlternateStaffId}> This user has been flagged! {config.WatchedIds[user.Id]}";
                        else
                            message = $"{message}\n<@&{config.StaffId}> This user has been flagged! {config.WatchedIds[user.Id]}";
                    }

                    await (client.GetGuild(config.HomeGuildId).GetChannel(config.MainChannelId) as ISocketMessageChannel)
                        .SendMessageAsync(message);
                    
                    if (user.Guild.VerificationLevel < VerificationLevel.Extreme)
                        return;

                    LoggedUser loggedUser = await dbhelper.GetLoggedUser(user.Id);

                    if (loggedUser == null)
                    {
                        loggedUser = await dbhelper.AddLoggedUser
                            (
                                user.Id,
                                newAccount: user.CreatedAt.Date > DateTimeOffset.Now.AddDays(config.MinimumAccountAge * -1)
                            );
                    }

                    if (loggedUser.ApprovedAccess)
                    {
                        // User that has already been in the server and gained approval

                        Console.WriteLine($"Found approved user {loggedUser.UserId}");

                        //try
                        //{
                        //    await user.SendMessageAsync(WelcomeBackMessage);
                        //}
                        //catch (Exception ex)
                        //{
                        //    Console.WriteLine($"Error sending welcome message to {user.Id}!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                        //}

                        try
                        {
                            var role = client.GetGuild(config.HomeGuildId).GetRole(config.AccessRoleId);

                            if (client.GetGuild(config.HomeGuildId).GetUser(user.Id) == null)
                            {
                                Console.WriteLine($"{user.Id} isn't in the server anymore!");
                                return;
                            }

                            RegisterNewUser(user.Id);
                            await user.AddRoleAsync(role);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error adding role to {user.Id}\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                        }
                    }
                    else if (!loggedUser.NewAccount)
                    {
                        // User that has never been in the server, but gets automatic approval
                        
                        Console.WriteLine("Found old account but new join");

                        //try
                        //{
                        //    await user.SendMessageAsync(WelcomeMessage);
                        //}
                        //catch (Exception ex)
                        //{
                        //    Console.WriteLine($"Error sending welcome message to {user.Id}!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                        //}

                        //CancellationTokenSource source = new CancellationTokenSource();
                        //waitingUsers.Add(user.Id, source);

                        //Task.Run(async () => DelayAddRole(user.Id, source.Token), source.Token);

                        try
                        {
                            var role = client.GetGuild(config.HomeGuildId).GetRole(config.AccessRoleId);

                            if (client.GetGuild(config.HomeGuildId).GetUser(user.Id) == null)
                            {
                                Console.WriteLine($"{user.Id} isn't in the server anymore!");
                                return;
                            }

                            RegisterNewUser(user.Id);
                            await user.AddRoleAsync(role);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error adding role to {user.Id}\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                        }

                        dbhelper.AutoApproveUser(user.Id);
                    }
                    else
                    {
                        // User that has been denied approval for whatever reason

                        Console.WriteLine("Found new account");

                        await Task.Delay(1000);

                        await (client.GetGuild(config.HomeGuildId).GetChannel(config.FilteredChannelId) as ISocketMessageChannel)
                            .SendMessageAsync($"<:marxist_think:305877855366152193> " +
                            $"**User Joined** `{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}`\n" +
                            $"{user.Username}#{user.Discriminator} ({user.Id}) ({user.Mention})\n" +
                            $"**Account created** `{user.CreatedAt.ToLocalTime().ToString("d")} {user.CreatedAt.ToLocalTime().ToString("T")}`");

                        try
                        {
                            await user.SendMessageAsync(NewUserNotice);
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

            var name = client.GetGuild(config.HomeGuildId)?.Name;

            if (name == null)
                // TODO: Add an error log here, we're not in our home guild anymore.
                return;

            if ((message.Channel as IGuildChannel) == null)
            {
                await Task.Delay(500);

                if (!messagedUsers.Contains(message.Author.Id))
                {
                    await message.Channel.SendMessageAsync($"I am a utility bot for {name}. I have few public commands, and am otherwise useless in DMs.\n" +
                        $"To report something to the moderators, please use the `!report` command here, " +
                        $"and please include any relevant details such as who is involved, what channel the event is taking place in, etc.");
                    messagedUsers.Add(message.Author.Id);
                }

                return;
            }
            else
            {
                if (!config.RemoveSpoilers)
                    return;

                try
                {
                    if (!pMsg.Channel.Name.ToLower().Contains("spoil") && ( pMsg.Content.Count(x => x == '|') > 3 || pMsg.Attachments.Any(x => x.Filename.StartsWith("SPOILER_"))))
                    {
                        await pMsg.DeleteAsync();

                        var channel = pMsg.Channel as ITextChannel;

                        var user = await channel.Guild.GetUserAsync(pMsg.Author.Id);
                        var role = user.GetRoles().Where(x => x.Color != Color.Default).OrderBy(x => x.Position).Last();
                        EmbedBuilder builder = new EmbedBuilder();
                        EmbedAuthorBuilder authorBuilder = new EmbedAuthorBuilder();

                        await channel.SendMessageAsync(embed:
                        builder
                            .WithAuthor(
                                authorBuilder.WithIconUrl(user.GetAvatarUrl(ImageFormat.Png) ?? user.GetDefaultAvatarUrl())
                                .WithName($"{user.Nickname ?? user.Username} said...")
                                )
                            .WithColor(role.Color)
                            .WithDescription((pMsg.Content.Replace('|', ' ') == "") ? "[No Text Content]" : "Some improper use of spoiler tags")
                            .WithFooter("Please do not use spoilers outside of spoiler channels.")
                            .WithTimestamp(pMsg.Timestamp)
                            .Build()
                                );
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting spoiler!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                }
            }
        }
    }
}

