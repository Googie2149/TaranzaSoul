using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.Commands.Builders;
using Discord.WebSocket;
using Discord.Addons.EmojiTools;
using Microsoft.Extensions.DependencyInjection;
//using Minitori.Modules.HelpModule;

namespace TaranzaSoul
{
    public class CommandHandler
    {
        private CommandService commands;
        private DiscordSocketClient client;
        //private IDependencyMap map;
        private IServiceProvider services;
        private Config config;

        public async Task Install(IServiceProvider _services)
        {
            // Create Command Service, inject it into Dependency Map
            client = _services.GetService<DiscordSocketClient>();
            commands = new CommandService();
            services = _services;
            config = _services.GetService<Config>();

            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);

            //await HelpModule.Install(commands);

            client.MessageReceived += HandleCommand;
        }

        public async Task HandleCommand(SocketMessage parameterMessage)
        {
            // Don't handle the command if it is a system message
            var message = parameterMessage as SocketUserMessage;
            if (message == null) return;

            // Mark where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message has a valid prefix, adjust argPos 
            if (!ParseTriggers(message, ref argPos)) return;

            // Create a Command Context
            var context = new MinitoriContext(client, message);
            // Execute the Command, store the result
            var result = await commands.ExecuteAsync(context, argPos, services);



            // If the command failed, notify the user
            //if (!result.IsSuccess)
            //    await message.Channel.SendMessageAsync($"**Error:** {result.ErrorReason}");
        }
        
        private bool ParseTriggers(SocketUserMessage message, ref int argPos)
        {
            bool flag = false;
            if (message.HasMentionPrefix(client.CurrentUser, ref argPos)) flag = true;
            else
            {
                foreach (var prefix in config.PrefixList)
                {
                    if (message.HasStringPrefix(prefix, ref argPos))
                    {
                        flag = true;
                        break;
                    }
                }
            };

            return flag;
        }
    }
}
