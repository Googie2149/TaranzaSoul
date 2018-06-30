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
    public class Standard : ModuleBase
    {
        [Command("blah")]
        [Summary("Blah!")]
        [Priority(1000)]
        public async Task Blah()
        {
            await ReplyAsync($"Blah to you too, {Context.User.Mention}.");
        }

        [Command("downloadusers")]
        public async Task Download()
        {
            Task.Run(async () =>
            {
                int before = ((SocketGuild)Context.Guild).Users.Count();
                await ((SocketGuild)Context.Guild).DownloadUsersAsync();

                int after = ((SocketGuild)Context.Guild).Users.Count();
                
                await ReplyAsync($"Downloaded {after - before} users");
            });
        }

        [Command("listroles")]
        public async Task ListRoles()
        {
            if (Context.User.Id != 102528327251656704)
                return;

            StringBuilder output = new StringBuilder();

            foreach (var r in Context.Guild.Roles.OrderByDescending(x => x.Position))
            {
                var temp = $"\"{r.Name}\": \"{r.Id}\"";

                if (output.Length + temp.Length + 6 > 2000)
                {
                    await ReplyAsync($"```{output.ToString()}```");
                    output.Clear();
                }

                output.AppendLine(temp);
            }

            await ReplyAsync($"```{output.ToString()}```");
        }

        [Command("updateroles")]
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

            await ReplyAsync($"Adding the {role.Name} role to {update.Count()} new friends!\n" +
                $"This should take a bit above {new TimeSpan(1200 * update.Count()).TotalMinutes} minutes.");

            Task.Run(async () =>
            {
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

                await ReplyAsync("Done! Don't forget to manually add the role to anyone that may have joined after the update.");
            });
        }

        [Command("soostime")]
        public async Task UpdateNames()
        {
            if (Context.User.Id != 102528327251656704)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var users = (await Context.Guild.GetUsersAsync()).Where(x => x.Nickname?.ToLower().Contains("susie") == false &&
                    x.Nickname?.ToLower().Contains("soos") == false);

                    await ReplyAsync($"THIS IS A BAD IDEA\n{users.Count()}");

                    foreach (var u in users)
                    {
                        try
                        {
                            await u.ModifyAsync(x => x.Nickname = "Susie");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Couldn't change {u.Username}#{u.Discriminator}");
                            Console.WriteLine($"{ex.Source?.ToString()}\n{ex.StackTrace}");
                        }

                        await Task.Delay(2000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Source?.ToString()}\n{ex.StackTrace}");
                }
            });
        }

        [Command("unsoostime")]
        public async Task UndoNames()
        {
            if (Context.User.Id != 102528327251656704)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var users = (await Context.Guild.GetUsersAsync()).Where(x => x.Nickname == "Susie");

                    await ReplyAsync($"THIS IS slightly less of a BAD IDEA\n{users.Count()}");

                    foreach (var u in users)
                    {
                        try
                        {
                            await u.ModifyAsync(x => x.Nickname = null);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Couldn't change {u.Username}#{u.Discriminator}");
                            Console.WriteLine($"{ex.Source?.ToString()}\n{ex.StackTrace}");
                        }

                        await Task.Delay(2000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Source?.ToString()}\n{ex.StackTrace}");
                }
            });
        }


        [Command("quit")]
        [Priority(1000)]
        public async Task ShutDown()
        {
            if (Context.User.Id != 102528327251656704)
            {
                await ReplyAsync(":no_good::skin-tone-3: You don't have permission to run this command!");
                return;
            }

            Task.Run(async () =>
            {
                await ReplyAsync("rip");
                //await Task.Delay(500);
                await ((DiscordSocketClient)Context.Client).LogoutAsync();
                Environment.Exit(0);
            });
        }
    }
}

