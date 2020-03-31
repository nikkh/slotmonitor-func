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
    public class SlotMonitorHttp
    {
        private readonly IConfiguration _config;
        private readonly MonitoringContext _monitoringContext;
        private readonly string _storageConnectionString;
        private readonly string _mailPassword;
        public SlotMonitorHttp(IConfiguration config, MonitoringContext monitoringContext)
        {
            _config = config;
            _monitoringContext = monitoringContext;
            _storageConnectionString = _config["StorageConnectionString"];
            _mailPassword = _config["MailPassword"];
        }
        [FunctionName("SlotMonitorHttp")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext ec)
        {
            try { 
            log.LogInformation($"{ec.FunctionName} (http trigger) function executed at: {DateTime.UtcNow}");
            var worker = new SlotMonitorWorker(_config, _monitoringContext, _storageConnectionString, _mailPassword);
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
