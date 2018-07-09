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

        [Command("Ping")]
        [Summary("Pong!")]
        [Priority(1000)]
        public async Task Blah()
        {
            await RespondAsync($"Pong {Context.User.Mention}!");
        }

        [Command("setnick")]
        [Summary("Change my nickname!")]
        [RequireOwner()]
        public async Task SetNickname(string Nick = "")
        {
            await (Context.Guild as SocketGuild).CurrentUser.ModifyAsync(x => x.Nickname = Nick);
            await RespondAsync(":thumbsup:");
        }

        [Command("quit", RunMode = RunMode.Async)]
        [Priority(1000)]
        [RequireOwner()]
        public async Task ShutDown()
        {
            await RespondAsync("Disconnecting...");
            await config.Save();
            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.Success);
        }

        [Command("restart", RunMode = RunMode.Async)]
        [Priority(1000)]
        [RequireOwner()]
        public async Task Restart()
        {
            await RespondAsync("Restarting...");
            await config.Save();
            await File.WriteAllTextAsync("./update", Context.Channel.Id.ToString());

            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.Restart);
        }

        [Command("update", RunMode = RunMode.Async)]
        [Priority(1000)]
        [RequireOwner()]
        public async Task UpdateAndRestart()
        {
            await File.WriteAllTextAsync("./update", Context.Channel.Id.ToString());

            await RespondAsync("Pulling latest code and rebuilding from source, I'll be back in a bit.");
            await config.Save();
            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.RestartAndUpdate);
        }

        [Command("deadlocksim", RunMode = RunMode.Async)]
        [Priority(1000)]
        [RequireOwner()]
        public async Task DeadlockSimulation()
        {
            File.Create("./deadlock");

            await RespondAsync("Restarting...");
            await config.Save();
            await Context.Client.LogoutAsync();
            await Task.Delay(1000);
            Environment.Exit((int)ExitCodes.ExitCode.DeadlockEscape);
        }

        [Command("downloadusers", RunMode = RunMode.Async)]
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
            if (Context.User.Id != 102528327251656704)
                return;

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
            if (Context.User.Id != 102528327251656704)
                return;

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

        [Command("raidtest", RunMode = RunMode.Async)]
        public async Task CheckRaiders()
        {
            if (Context.User.Id != 102528327251656704)
                return;

            var blah = JsonStorage.DeserializeObjectFromFile<List<JsonList>>("users.json");

            await Context.Guild.DownloadUsersAsync();

            var list = Context.Guild.Users.Where(x => x.IsBot == false && blah.Select(y => y.user.id).Contains(x.Id));

            await RespondAsync(string.Join('\n', list.OrderByDescending(x => x.JoinedAt).Select(x => $"`{x.Id}` | {x.Mention} | {x.Username} | {x.JoinedAt.ToString()}")));
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

