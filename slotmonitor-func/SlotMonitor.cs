using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using MimeKit;
using Newtonsoft.Json.Linq;
using slotmonitor_func.Contexts;
using slotmonitor_func.Models;

namespace slotmonitor_func
{
    public class SlotMonitor
    {
        private readonly IConfiguration _config;
        private readonly MonitoringContext _monitoringContext;
        private readonly string _storageConnectionString;
        private readonly string _mailPassword;

        const string FUNCTION_NAME = "[SlotMonitor]";

        public SlotMonitor(IConfiguration config, MonitoringContext monitoringContext)
        {
            _config = config;
            _monitoringContext = monitoringContext;
            _storageConnectionString = _config["StorageConnectionString"];
            _mailPassword = _config["MailPassword"];
        }

        [FunctionName("SlotMonitor")]
        public async Task Run([TimerTrigger("0 */30 * * * *")]TimerInfo myTimer, ILogger log, ExecutionContext ec)
        {
            try
            {
                log.LogInformation($"{ec.FunctionName} (timer trigger) function executed at: {DateTime.Now}");
                var worker = new SlotMonitorWorker(_config, _monitoringContext, _storageConnectionString, _mailPassword);
                await worker.Run(log, ec.FunctionName);
                return;
            }
            catch(Exception e)
            {
                log.LogError($"Exeception during execution of {ec.FunctionName}. Message: {e.Message}. Check Inner Exception", e);
            }
        }
    }
}
