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

        private void WatchListHelper(string remainder, out List<ulong> users, out string note)
        {
            var args = remainder.Split(' ').Where(x => x.Length > 0).ToList();
            note = "";
            users = new List<ulong>();
            
            foreach (var s in new List<string>(args))
            {
                var id = s.TrimStart('<').TrimStart('@').TrimStart('!').TrimEnd('>');
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

        [Command("watch")]
        [Summary("idk go watch some tv")]
        [Priority(1000)]
        public async Task WatchList([Remainder]string remainder = "")
        {
            //451057945044582400

            if (Context.Guild.Id != 132720341058453504)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(451057945044582400))
                return;

            List<ulong> users;
            string note;

            WatchListHelper(remainder, out users, out note);

            if (users.Count() == 0)
            {
                await RespondAsync("None of those mentioned were valid user Ids!");
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
            if (Context.Guild.Id != 132720341058453504)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(451057945044582400))
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
            if (Context.Guild.Id != 132720341058453504)
                return;

            if (!((IGuildUser)Context.User).RoleIds.ToList().Contains(451057945044582400))
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

