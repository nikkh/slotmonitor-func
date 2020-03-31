using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using slotmonitor_func.Contexts;
using System.Net;

namespace slotmonitor_func
{
    public class ZodiacGeneratorHttp
    {
        private readonly ZodiacContext _zodiacContext;
        public ZodiacGeneratorHttp(IConfiguration config, ZodiacContext zodiacContext)
        {
            _zodiacContext = zodiacContext;
        }

        [FunctionName("ZodiacGeneratorHttp")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
             ILogger log, ExecutionContext ec)
        {
            try
            {
                log.LogInformation($"{ec.FunctionName} (http trigger) function executed at: {DateTime.UtcNow}");
                var worker = new ZodiacWorker(_zodiacContext);
                await worker.Run(log, ec.FunctionName);
            }
            catch (Exception e)
            {
                log.LogError($"Exeception during execution of {ec.FunctionName}. Message: {e.Message}. Check Inner Exception", e);
            }
            return new OkObjectResult(HttpStatusCode.OK);
        }
    }
}
