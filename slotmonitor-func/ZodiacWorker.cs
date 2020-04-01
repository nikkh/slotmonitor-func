﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using slotmonitor_func.Contexts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace slotmonitor_func
{
    internal class ZodiacWorker
    {
        private readonly ZodiacContext _zodiacContext;

        internal ZodiacWorker(ZodiacContext zodiacContext)
        {
            _zodiacContext = zodiacContext;
        }
        internal async Task Run(ILogger log, string triggerFunction)
        {
            Random random = new Random();
            int randomNumber = random.Next(0, 1000);
            int maxCalls = _zodiacContext.NumberOfCallsPerInvocation;
            int calls = maxCalls > randomNumber ? randomNumber : maxCalls;
            log.LogInformation($"Zodiac Worker function was invoked by {triggerFunction} at: {DateTime.UtcNow}");
            string[] pageMaster = { "aries", "cancer", "taurus", "gemini", "leo", "virgo", "libra", "scorpio", "sagittarius", "capricorn", "pisces", "aquarius" };
            log.LogInformation($"About to generate {calls} calls.");
            List<string> pages = new List<string>();
            for (int i = 0; i < calls; i++)
            {
                int min = 1;
                int max = pageMaster.Length + 1;
                int index = random.Next(min, max);
                var traceGuid = Guid.NewGuid().ToString();
                var pageString = $"{pageMaster[index - 1]}?traceGuid=INSIGHTSGENERATOR: {traceGuid}";
                if (pageMaster[index - 1] == "pisces")
                {
                    if (shouldMaxCPU()) pageString += "&cpumax=true"; else pageString += "&cpumax=false";
                }
                pages.Add(pageString);
            }

            int j = 0;
            foreach (var page in pages)
            {
                j++;
                await RestApi.Call(_zodiacContext.BaseUrl, page, log);
                if (j % 25 == 0) log.LogDebug($"{j} calls have been made");
            }
            log.LogInformation($"Done. {j} calls were made in total.");
            return;
        }
        private static bool shouldMaxCPU()
        {
            int min = 1;
            int max = 5;
            Random random = new Random();
            int index = random.Next(min, max);
            if (index == 3) return true;
            return false;
        }
    }
}