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

namespace TaranzaSoul.Modules.Standard
{
    public class Standard : MinitoriModule
    {
        private CommandService commands;
        private IServiceProvider services;
        private Config config;
        private DatabaseHelper dbhelper;
        private Logger logger;

        enum FakePunishments
        {
            None = 0,
            tempmuted = 30,
            kicked = 55,
            tempbanned = 75,
            banned = 90
        }

        public Standard(CommandService _commands, IServiceProvider _services, Config _config, DatabaseHelper _dbhelper, Logger _logger)
        {
            commands = _commands;
            services = _services;
            config = _config;
            dbhelper = _dbhelper;
            logger = _logger;
        }

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

        [Command("help")]
        public async Task HelpCommand()
        {
            Context.IsHelp = true;

            StringBuilder output = new StringBuilder();
            StringBuilder module = new StringBuilder();
            var SeenModules = new List<string>();
            int i = 0;

            output.Append("These are the commands you can use:");

            foreach (var c in commands.Commands)
            {
                if (!SeenModules.Contains(c.Module.Name))
                {
                    if (i > 0)
                        output.Append(module.ToString());

                    module.Clear();

                    module.Append($"\n**{c.Module.Name}:**");
                    SeenModules.Add(c.Module.Name);
                    i = 0;
                }

                if ((await c.CheckPreconditionsAsync(Context, services)).IsSuccess)
                {
                    if (i == 0)
                        module.Append(" ");
                    else
                        module.Append(", ");

                    i++;

                    module.Append($"`{c.Name}`");
                }
            }

            if (i > 0)
                output.AppendLine(module.ToString());

            await RespondAsync(output.ToString());
        }

        [Command("ping")]
        [Summary("Pong!")]
        [Priority(1000)]
        public async Task Blah()
        {
            await RespondAsync($"Pong {Context.User.Mention}!");
        }

        private void WatchListHelper(string remainder, out List<ulong> users, out string note)
        {
            var args = remainder.Split(' ').Where(x => x.Length > 0).ToList();
            note = "";
            users = new List<ulong>();

            foreach (var s in new List<string>(args))
            {
                var id = s.TrimStart('\\').TrimStart('<').TrimStart('@').TrimStart('!').TrimEnd('>');
                ulong temp;
                if (ulong.TryParse(id, out temp))
                {
                    //var u = Context.Guild.GetUser(temp);

                    //if (u != null)
                    //    users.Add(u);

                    users.Add(temp);

                    args.RemoveAt(0);
                }
                else
                    break;
            }

            if (users.Count() == 0)
                return;
            else
                note = string.Join(" ", args).Trim();
        }

        [Command("approve", RunMode = RunMode.Async)]
        public async Task ApproveNewUserAccess([Remainder]string remainder = "")
        {
            if (Context.Guild.Id != config.HomeGuildId)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(451058863995879463))
                return;

            List<ulong> users;
            string note;

            WatchListHelper(remainder, out users, out note);

            if (users.Count() == 0)
            {
                await RespondAsync("None of those mentioned were valid user Ids!");
                return;
            }

            if (users.Count() > 1)
            {
                await RespondAsync("You're not supposed to approve more than one user with a single note.");
                return;
            }

            if (note == "")
            {
                await RespondAsync("The note cannot be blank!");
                return;
            }

            if (Context.Guild.GetUser(users.First()) == null)
            {
                await RespondAsync("That user isn't in this server!");
                return;
            }

            var loggedUser = await dbhelper.GetLoggedUser(users.First());

            if (loggedUser == null)
            {
                await RespondAsync("That user hasn't been seen in this server.");
                return;
            }
            else
            {
                if (loggedUser.ApprovedAccess)
                {
                    await RespondAsync("That user already is already approved!");
                    return;
                }
            }

            var role = Context.Guild.GetRole(config.AccessRoleId);

            await dbhelper.ModApproveUser(users.First(), Context.User.Id, note);
            await Task.Delay(500);
            await Context.Guild.GetUser(users.First()).AddRoleAsync(role);
            await RespondAsync($"{Context.Guild.GetUser(users.First()).Mention} has been approved access to the server.");

            logger.RegisterNewUser(users.First());
        }

        [Command("revoke", RunMode = RunMode.Async)]
        public async Task RevokeUserAccess([Remainder]string remainder = "")
        {
            if (Context.Guild.Id != config.HomeGuildId)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(451058863995879463))
                return;

            List<ulong> users;
            string note;

            WatchListHelper(remainder, out users, out note);

            if (users.Count() == 0)
            {
                await RespondAsync("None of those mentioned were valid user Ids!");
                return;
            }

            //if (users.Count() > 1)
            //{
            //    await RespondAsync("You're not supposed to approve more than one user with a single note.");
            //    return;
            //}

            //if (note == "")
            //{
            //    await RespondAsync("The note cannot be blank!");
            //    return;
            //}

            //if ((await dbhelper.GetLoggedUser(users.First())) == null)
            //{
            //    await RespondAsync("That user hasn't been seen in this server.");
            //    return;
            //}

            StringBuilder unrecognized = new StringBuilder();
            StringBuilder removed = new StringBuilder();

            var role = Context.Guild.GetRole(config.AccessRoleId);

            foreach (var u in users)
            {
                if ((await dbhelper.GetLoggedUser(u) == null))
                {
                    if (unrecognized.Length == 0)
                        unrecognized.Append("The following user Ids(s) were not recognized: ");

                    unrecognized.Append($"{u} ");
                }
                else
                {
                    if (removed.Length == 0)
                        removed.Append("Removed the following user Id(s): ");

                    removed.Append($"{u} ");

                    await dbhelper.RevokeApproval(u);
                    await Context.Guild.GetUser(u).RemoveRoleAsync(role);
                }
            }

            await RespondAsync($"{unrecognized.ToString()}\n{removed.ToString()}");
        }

        [Command("togglespoilers")]
        public async Task ToggleSpoilerRemoval()
        {
            if (Context.Guild.Id != config.HomeGuildId)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(config.StaffId))
                return;

            config.RemoveSpoilers = !config.RemoveSpoilers;
            await config.Save();

            await RespondAsync($"Set `remove_spoilers` to `{config.RemoveSpoilers}`");
        }

        [Command("watch")]
        [Summary("idk go watch some tv")]
        [Priority(1000)]
        public async Task WatchList([Remainder]string remainder = "")
        {
            //451057945044582400

            if (Context.Guild.Id != config.HomeGuildId)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(config.StaffId))
                return;

            List<ulong> users;
            string note;

            WatchListHelper(remainder, out users, out note);

            if (users.Count() == 0)
            {
                await RespondAsync("None of those mentioned were valid user Ids!");
                return;
            }

            if (note != "") // this means we ARE setting a note on one or more people
            {
                StringBuilder output = new StringBuilder();

                output.AppendLine($"Added the reason {note} - {Context.User.Username}#{Context.User.Discriminator} to the following user Id(s): " +
                    $"{users.Select(x => $"`{x.ToString()}`").Join(", ")}");

                foreach (var u in users)
                {
                    if (config.WatchedIds.ContainsKey(u))
                    {
                        output.AppendLine($"`{u}` already had a note! It was: {config.WatchedIds[u]}");
                    }

                    config.WatchedIds[u] = note;
                }

                await RespondAsync(output.ToString());

                await config.Save();
            }
            else // We are checking the contents of specifc notes, NOT setting any
            {
                StringBuilder output = new StringBuilder();

                foreach (var u in users)
                {
                    if (config.WatchedIds.ContainsKey(u))
                    {
                        output.AppendLine($"Note for `{u}`: {config.WatchedIds[u]}");
                    }
                    else
                        output.AppendLine($"`{u}` does not currently have a note set.");
                }

                await RespondAsync(output.ToString());
            }
        }

        [Command("watch clear")]
        [Summary("tv is boring")]
        [Priority(1001)]
        public async Task ClearWatch([Remainder]string remainder = "")
        {
            if (Context.Guild.Id != config.HomeGuildId)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(config.StaffId))
                return;

            List<ulong> users;
            string note;
            WatchListHelper(remainder, out users, out note);

            if (users.Count() == 0)
            {
                await RespondAsync("None of those mentioned were valid user Ids!");
                return;
            }

            StringBuilder output = new StringBuilder();

            foreach (var u in users)
            {
                if (config.WatchedIds.ContainsKey(u))
                {
                    output.AppendLine($"Note cleared for `{u}`: {config.WatchedIds[u]}");
                    config.WatchedIds.Remove(u);
                }
                else
                    output.AppendLine($"`{u}` did not have a note.");
            }

            await RespondAsync(output.ToString());

            await config.Save();
        }

        [Command("watch all")]
        [Summary("tivo guide!!!!!")]
        [Priority(1001)]
        public async Task ListWatch()
        {
            if (Context.Guild.Id != config.HomeGuildId)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(config.StaffId))
                return;

            StringBuilder output = new StringBuilder();

            foreach (KeyValuePair<ulong, string> kv in config.WatchedIds)
            {
                output.AppendLine($"Note for `{kv.Key}`: {kv.Value}");
            }

            await RespondAsync(output.ToString());
        }

        [Command("setnick")]
        [Summary("Change my nickname!")]
        public async Task SetNickname(string Nick = "")
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            await (Context.Guild as SocketGuild).CurrentUser.ModifyAsync(x => x.Nickname = Nick);
            await RespondAsync(":thumbsup:");
        }

        [Command("quit", RunMode = RunMode.Async)]
        [Priority(1000)]
        public async Task ShutDown()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            await RespondAsync("Disconnecting...");
            await config.Save();
            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.Success);
        }

        [Command("restart", RunMode = RunMode.Async)]
        [Priority(1000)]
        public async Task Restart()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            await RespondAsync("Restarting...");
            await config.Save();
            await File.WriteAllTextAsync("./update", Context.Channel.Id.ToString());

            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.Restart);
        }

        [Command("update", RunMode = RunMode.Async)]
        [Priority(1000)]
        public async Task UpdateAndRestart()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            await File.WriteAllTextAsync("./update", Context.Channel.Id.ToString());

            await RespondAsync("Pulling latest code and rebuilding from source, I'll be back in a bit.");
            await config.Save();
            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.RestartAndUpdate);
        }

        [Command("deadlocksim", RunMode = RunMode.Async)]
        [Priority(1000)]
        public async Task DeadlockSimulation()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            File.Create("./deadlock");

            await RespondAsync("Restarting...");
            await config.Save();
            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.DeadlockEscape);
        }

        [Command("register")]
        [Priority(1000)]
        public async Task AddFriendCode(string FriendCode = "", [Remainder]string SwitchName = "")
        {
            //if (Context.Channel.Id != 417458111553470474)
            //{
            //    RespondAsync("This only works in <#417458111553470474> fow now, sorry!");
            //    return;
            //}

            try
            {
                if (FriendCode == "")
                {
                    RespondAsync($"{Context.User.Mention} your friend code can't be left blank!");
                    return;
                }

                string temp = FriendCode.ToLower().Replace("-", "").Replace(".", "").Replace("sw", "");

                ulong parsedFriendCode = 0;

                if (temp.Length == 12 && ulong.TryParse(temp, out parsedFriendCode))
                {


                    if (SwitchName.Length > 10)
                    {
                        RespondAsync($"{Context.User.Mention} that's too long for a Switch Nickname!");
                        return;
                    }

                    await dbhelper.InitializedFCDB();

                    var user = await dbhelper.GetSwitchFC(Context.User.Id);

                    IUserMessage message;

                    var channel = Context.Client.GetChannel(619088469339144202) as SocketTextChannel;

                    if (config.FCPinnedMessageId == 0)
                    {
                        message = await channel.SendMessageAsync("Please wait...");

                        config.FCPinnedMessageId = message.Id;
                        if (message.Id < 5)
                            await Task.Delay(1000);

                        await Config.Save();
                    }
                    else
                    {
                        message = await channel.GetMessageAsync(config.FCPinnedMessageId) as IUserMessage;

                        if (message == null)
                        {
                            await Task.Delay(1000);
                            if (message == null)
                            {
                                //await RespondAsync("Well this is embarassing, I can't seem to fetch the pinned message. Hold tight.");
                                //return;

                                message = await channel.SendMessageAsync("Please wait...");

                                if (message?.Id == null || message.Id < 5)
                                    await Task.Delay(1000);

                                config.FCPinnedMessageId = message.Id;

                                await Config.Save();
                            }
                        }

                        //if (message == null)
                        //{
                        //    message = await ReplyAsync("Googie was here :^)");
                        //}

                        //config.FCPinnedMessageId = message.Id;
                    }

                    if (user == null)
                        await dbhelper.AddFriendCode(Context.User.Id, parsedFriendCode, message.Id, SwitchName);
                    else
                        await dbhelper.EditFriendCode(Context.User.Id, parsedFriendCode, message.Id, SwitchName);

                    var AllFCs = await dbhelper.GetAllFriendCodes();

                    StringBuilder output = new StringBuilder();
                    output.AppendLine("Please only friend people with their permission.\nTo add yourself, use `@Secretary Susie register 0000-0000-0000 SwitchName`");

                    foreach (var kv in AllFCs)
                    {
                        output.AppendLine($"<@{kv.Key}>: `{kv.Value.FriendCode.ToString("0000-0000-0000")}` {kv.Value.SwitchNickname}");
                    }

                    await message.ModifyAsync(x => x.Content = output.ToString());

                    await message.PinAsync();

                    if (Context.Channel.Id != 619088469339144202)
                        await RespondAsync($"{Context.User.Mention} you've been added! Check the pins in <#619088469339144202>!");
                    else
                        await RespondAsync($"{Context.User.Mention} you've been added! Check the pins!");
                }
                else
                {
                    RespondAsync($"{Context.User.Mention} I can't read that as a friend code!");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Blah!\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}\n{ex.StackTrace}\n{ex.TargetSite}");
            }
        }

        [Command("fc")]
        [Priority(1000)]
        public async Task GetFC([Remainder]string remainder = "")
        {
            SocketGuildUser user;

            if (Context.Message.MentionedUserIds.Where(x => x != Context.Client.CurrentUser.Id).Count() > 1)
            {
                await RespondAsync("I can currently under development and can only return a single friend code at a time. Try mentioning only one person, or checking the pins in <#619088469339144202>.");
                return;
            }
            else if (Context.Message.MentionedUserIds.Where(x => x != Context.Client.CurrentUser.Id).Count() == 1)
            {
                user = Context.Guild.GetUser(Context.Message.MentionedUserIds.Where(x => x != Context.Client.CurrentUser.Id).First());
            }
            else if (Context.Message.MentionedUserIds.Where(x => x != Context.Client.CurrentUser.Id).Count() == 0)
            {
                user = Context.User as SocketGuildUser;
            }
            else
            {
                await RespondAsync("Yeah uh, this code is never supposed to be reached. If you see it, just ping some mods and they'll yell at me.");
                return;
            }

            SwitchUser FriendCode = await dbhelper.GetSwitchFC(user.Id);

            if (FriendCode == null)
            {
                if (user.Id == Context.User.Id)
                    await RespondAsync($"You haven't registered yourself! Use `@Secretary Susie register 0000-0000-0000 SwitchName` to set it up.");
                else
                    await ReplyAsync("They haven't registered their friend code yet!");

                return;
            }

            if (user.Id == Context.User.Id)
                await RespondAsync($"{Context.User.Mention}: `{FriendCode.FriendCode.ToString("0000-0000-0000")}` {FriendCode.SwitchNickname}");
            else
                await RespondAsync($"{Context.User.Mention}: {user.Nickname ?? user.Username}'s Friend Code is `{FriendCode.FriendCode.ToString("0000-0000-0000")}` {FriendCode.SwitchNickname}");
        }

        [Command("report")]
        [Priority(1000)]
        [RequireContext(ContextType.DM)]
        public async Task Report([Remainder]string remainder = "")
        {
            try
            {
                if (remainder.Length < 10)
                {
                    await RespondAsync("To prevent abuse, reports must be a minimum of 10 characters.");
                    return;
                }

                SocketTextChannel channel = Context.Client.GetGuild(config.HomeGuildId).GetChannel(config.ReportChannelId) as SocketTextChannel;

                EmbedBuilder builder = new EmbedBuilder();

                builder.Title = "DM Report!";
                builder.AddField("Content:", $"New report from {Context.User.Mention}:\n{remainder}");

                Console.WriteLine("content added");

                if (Context.Message.Attachments.Count() > 0)
                {
                    StringBuilder output = new StringBuilder();

                    foreach (var a in Context.Message.Attachments)
                    {
                        output.AppendLine(a.Url);
                    }

                    builder.AddField("Attachments:", output.ToString());
                }

                var user = Context.Client.GetGuild(config.HomeGuildId).GetUser(Context.User.Id);

                builder.WithFooter($"Sent by {user.Nickname ?? Context.User.Username}#{Context.User.Discriminator}", Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl());
                builder.Timestamp = DateTimeOffset.Now;

                Discord.Color color;

                var allRoles = user.GetRoles().Where(x => x.Color != Color.Default);
                if (allRoles.Count() == 0)
                    color = Color.Default;
                else
                    color = allRoles.OrderBy(x => x.Position).Last().Color;

                builder.Color = color;

                await channel.SendMessageAsync(embed: builder.Build());

                await RespondAsync("Report sent!");

                return;
            }
            catch (Exception ex)
            {
                Log(ex);
                await RespondAsync("I had an issue sending that report, try again later.\nIf this is an emergency, please ping the @Mods role instead.");
                return;
            }


        }

        [Command("strike")]
        [Alias("strikey", "zap")]
        [Priority(1000)]
        public async Task FakeStrike([Remainder]string remainder = "")
        {
            if (remainder == "")
            {
                await RespondAsync("<:vError:625705714324865024> Please provide at least one user!");
                return;
            }

            bool zap = false;

            if (Context.Message.Content.Remove(remainder).ToLower().Contains("zap") || Context.Message.Content.Remove(remainder).ToLower().Contains("strikey"))
            {
                zap = true;
            }

            int strikes = 1;
            List<ulong> users;
            string note;

            if (Int32.TryParse(remainder.Split(' ').First(), out int temp))
            {
                if (temp < 0 || temp > 100)
                {
                    await RespondAsync("<:vError:625705714324865024> Number of strikes must be between 1 and 100!");
                    return;
                }
                else
                {
                    strikes = temp;
                    remainder = remainder.Substring(remainder.Split(' ').First().Length).Trim();
                }
            }

            WatchListHelper(remainder, out users, out note);

            if (users.Count() == 0)
            {
                await RespondAsync("<:vError:625705714324865024> Please provide at least one user!");
                return;
            }

            if (note == null || note == "")
            {
                await RespondAsync("<:vError:625705714324865024> Please provide a reason for the strike(s)!");
                return;
            }

            int callingPosition = (Context.User as SocketGuildUser).GetRoles().OrderByDescending(x => x.Position).FirstOrDefault().Position;

            List<IUser> strikedUsers = new List<IUser>();

            StringBuilder output = new StringBuilder();

            foreach (var u in users.Distinct())
            {
                IUser user = Context.Guild.GetUser(u);

                if (user != null)
                {
                    if (callingPosition < (user as SocketGuildUser).GetRoles().Where(x => x.Permissions.RawValue != 0).OrderByDescending(x => x.Position).FirstOrDefault().Position)
                    {
                        output.AppendLine($"<:vError:625705714324865024> You do not have permission to interact with **{user.Username}**#{user.Discriminator}");
                    }
                    else
                    {
                        if (user.IsBot)
                            output.AppendLine($"<:vError:625705714324865024> Strikes cannot be given to bots (**{user.Username}**#{user.Discriminator} (ID:{user.Id}))");
                        else
                        {
                            output.AppendLine($"<:vSuccess:625705714429722624> Successfully gave `{strikes}` strikes to **{user.Username}**#{user.Discriminator}");
                            strikedUsers.Add(user);
                        }
                    }
                }
                else
                {
                    user = Context.Client.GetUser(u);

                    if (user == null)
                        continue;

                    if (user.IsBot)
                        output.AppendLine($"<:vError:625705714324865024> Strikes cannot be given to bots (**{user.Username}**#{user.Discriminator} (ID:{user.Id}))");
                    else
                    {
                        output.AppendLine($"<:vSuccess:625705714429722624> Successfully gave `{strikes}` strikes to **{user.Username}**#{user.Discriminator}");
                        strikedUsers.Add(user);
                    }
                }
            }

            if (strikedUsers.Count() == 0)
            {
                if (!zap)
                    await RespondAsync(output.ToString());
                else
                    await RespondAsync(output.ToString().ToLower().Replace("strike", "zap"));
                return;
            }
            else if (zap && strikedUsers.Count() > 1)
            {
                await RespondAsync($"{Context.User.Mention} my dude you are trying to wield way too much power take a chill pill or somethin");
                return;
            }

            EmbedBuilder builder = new EmbedBuilder();

            builder.Title = "Strike DM Preview";
            string guildName = Context.Guild.Name;
            Random asdf = new Random();

            foreach (var u in strikedUsers)
            {
                if (zap)
                {
                    builder.AddField("The Legend", $"<:zap1:625749181142794250><:zap2:625749181109108756>Once upon a time, a poor **{guildName}** user violated a rule in desperation, only to be struck by the mods.\n" +
                        $"<:zap1:625749181142794250><:zap2:625749181109108756>As they breathed their last, a mysterious traveler appeared and unlocked their natural talent for `{note}`\n" +
                        $"<:zap1:625749181142794250><:zap2:625749181109108756>**ZAP!** *{u.Username}* was born.");

                    continue;
                }

                FakePunishments blah;
                var punishment = asdf.Next(0, 100);

                if (punishment < 30)
                    blah = FakePunishments.None;
                else if (punishment >= 30 && punishment < 55)
                    blah = FakePunishments.tempmuted;
                else if (punishment >= 55 && punishment < 75)
                    blah = FakePunishments.kicked;
                else if (punishment >= 75 && punishment < 90)
                    blah = FakePunishments.tempbanned;
                else if (punishment >= 90)
                    blah = FakePunishments.banned;
                else
                    blah = FakePunishments.None;

                StringBuilder punishmentGenerator = new StringBuilder();

                punishmentGenerator.AppendLine($"<:vWarning:625705714274271254> You have received `{strikes}` strikes in **{guildName}** for: `{note}`");

                switch (blah)
                {
                    case FakePunishments.tempmuted:
                        punishmentGenerator.Append("🤐 ");
                        break;
                    case FakePunishments.kicked:
                        punishmentGenerator.Append("👢 ");
                        break;
                    case (FakePunishments.tempbanned):
                        punishmentGenerator.Append("⏲ ");
                        break;
                    case (FakePunishments.banned):
                        punishmentGenerator.Append("🔨 ");
                        break;
                }

                switch (blah)
                {
                    case FakePunishments.tempmuted:
                    case FakePunishments.tempbanned:
                        int qwerty = asdf.Next(0, 100);
                        string units = "years";
                        int time = 1000;

                        if (qwerty < 50)
                        {
                            units = "minutes";
                            time = asdf.Next(1, 7) * 10;
                        }
                        else if (qwerty > 50)
                        {
                            units = "hours";
                            time = asdf.Next(2, 25);
                        }

                        punishmentGenerator.Append($"You have been **{blah.ToString()}** for **{time}** {units} from **{guildName}**");
                        break;
                    case FakePunishments.kicked:
                    case FakePunishments.banned:
                        punishmentGenerator.Append($"You have been **{blah.ToString()}** from **{guildName}**");
                        break;
                }

                builder.AddField($"{u.ToString()}", punishmentGenerator.ToString());
            }

            if (!zap)
                await ReplyAsync(message: output.ToString(), embed: builder.Build());
            else
            {
                builder.Title = "Zap DM Preview";
                await ReplyAsync(message: output.ToString().ToLower().Replace("strike", "zap"), embed: builder.Build());
            }
        }

        [Command("hey")]
        public async Task WHY()
        {
            await RespondAsync("listen!");
        }
    }

    public class JsonUser
    {
        public string username { get; set; }
        public string discriminator { get; set; }
        public ulong id { get; set; }
        public string avatar { get; set; }
    }

    public class JsonList
    {
        public string nick { get; set; }
        public JsonUser user { get; set; }
        public List<string> roles { get; set; }
        public bool mute { get; set; }
        public bool deaf { get; set; }
        public DateTime joined_at { get; set; }
    }
}

