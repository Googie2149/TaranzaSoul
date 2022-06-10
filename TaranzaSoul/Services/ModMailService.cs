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

namespace TaranzaSoul.Services
{
    public class ModMailService
    {
        private DiscordSocketClient socketClient;
        private DiscordSocketRestClient restClient;
        private IServiceProvider services;
        private Config config;

        public async Task Install(IServiceProvider _services)
        {
            socketClient = _services.GetService<DiscordSocketClient>();
            restClient = _services.GetService<DiscordSocketRestClient>();
            config = _services.GetService<Config>();
            services = _services;
        }

    }
}
