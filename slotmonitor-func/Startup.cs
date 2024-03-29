﻿using System;
using slotmonitor_func.Contexts;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
[assembly: FunctionsStartup(typeof(slotmonitor_func.Startup))]
namespace slotmonitor_func
{
    public class Startup : FunctionsStartup
    {

        public override void Configure(IFunctionsHostBuilder builder)
        {
            var localRoot = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot");
            var azureRoot = $"{Environment.GetEnvironmentVariable("HOME")}/site/wwwroot";

            var actualRoot = localRoot ?? azureRoot;

            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(actualRoot)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();
            IConfiguration configuration = configBuilder.Build();
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(configuration.GetSection("MonitoringContext").Get<MonitoringContext>());
            builder.Services.AddSingleton(configuration.GetSection("ZodiacContext").Get<ZodiacContext>());

        }
    }
}
