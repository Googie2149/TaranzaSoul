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

        public Standard(CommandService _commands, IServiceProvider _services, Config _config)
        {
            commands = _commands;
            services = _services;
            config = _config;
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

        //[Command("downloadusers", RunMode = RunMode.Async)]
        public async Task Download()
        {
            int before = ((SocketGuild)Context.Guild).Users.Count();
            await ((SocketGuild)Context.Guild).DownloadUsersAsync();

            int after = ((SocketGuild)Context.Guild).Users.Count();

            await RespondAsync($"Downloaded {after - before} users");
        }

        [Command("listroles")]
        public async Task ListRoles()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            StringBuilder output = new StringBuilder();

            foreach (var r in Context.Guild.Roles.OrderByDescending(x => x.Position))
            {
                output.AppendLine($"\"{r.Name}\": \"{r.Id}\"");
            }

            await RespondAsync($"```{output.ToString()}```");
        }

        [Command("updateroles", RunMode = RunMode.Async)]
        public async Task UpdateRoles()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            var update = new List<IGuildUser>();
            var role = ((SocketGuild)Context.Guild).GetRole(346373986604810240);

            foreach (var u in ((SocketGuild)Context.Guild).Users)
            {
                if (!u.Roles.Contains(role) && u.CreatedAt.Date < DateTimeOffset.Now.AddDays(-14))
                    update.Add(u);
            }

            await RespondAsync($"Adding the {role.Name} role to {update.Count()} new friends!\n" +
                $"This should take a bit above {new TimeSpan(1200 * update.Count()).TotalMinutes} minutes.");

            foreach (var u in update)
            {
                try
                {
                    await u.AddRoleAsync(role);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                await Task.Delay(1200);
            }

            await RespondAsync("Done! Don't forget to manually add the role to anyone that may have joined after the update.");
        }

        //[Command("raidtest", RunMode = RunMode.Async)]
        public async Task CheckRaiders()
        {
            if (!config.OwnerIds.Contains(Context.User.Id))
            {
                await RespondAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            var blah = JsonStorage.DeserializeObjectFromFile<List<JsonList>>("users.json");

            await Context.Guild.DownloadUsersAsync();
            var filter = new List<ulong>() { 133020327952252928, 344284382384881665, 185162109397499904 };

            var list = Context.Guild.Users.Where(x => x.IsBot == false && blah.Select(y => y.user.id).Contains(x.Id) && filter.Contains(x.Id) == false);

            await RespondAsync(string.Join('\n', list.OrderByDescending(x => x.JoinedAt).Select(x => $"`{x.Id}` | {x.Mention} | {x.Username} | {x.JoinedAt.ToString()}")));

            foreach (var u in list)
            {
                await u.BanAsync(7, "Member of a raid server");
                //await Task.Delay(1000);
            }

            var list2 = blah.Where(x => Context.Guild.Users.Select(y => y.Id).Contains(x.user.id) == false);

            await RespondAsync(string.Join('\n', list2.OrderByDescending(x => x.joined_at).Select(x => $"`{x.user.id}` | <@{x.user.id}> | {x.user.username} | {x.joined_at.ToString()}")));

            foreach (var u in list2)
            {
                await Context.Guild.AddBanAsync(u.user.id, reason: "Member of a raid server");
                await Task.Delay(1500);
            }
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

