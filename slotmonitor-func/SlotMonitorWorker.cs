using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using MimeKit;
using Newtonsoft.Json.Linq;
using slotmonitor_func.Contexts;
using slotmonitor_func.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace slotmonitor_func
{
    internal class SlotMonitorWorker
    {
        private readonly IConfiguration _config;
        private readonly MonitoringContext _monitoringContext;
        private readonly string _storageConnectionString;
        private readonly string _mailPassword;
        
        internal SlotMonitorWorker(IConfiguration config, MonitoringContext monitoringContext, string storageConnectionString, string mailPassword) 
        {
            _config = config;
            _monitoringContext = monitoringContext;
            _storageConnectionString = storageConnectionString;
            _mailPassword = mailPassword;
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
        }

        internal async Task Run(ILogger log, string triggerFunction) 
        {
            DateTime lastSlotPreviously = await GetPreviousSlotDate();
            DateTime lastSlotCurrently = DateTime.MaxValue;
            log.LogInformation($"Slot Monitor Worker function was invoked by {triggerFunction} at: {DateTime.UtcNow.ToLocalTime()}");

            string body = await CheckForSlots();
            var slots = ParseSlots(body);

            var freeSlots = slots.Where(s => s.Status != "UNAVAILABLE");


            if (freeSlots != null && freeSlots.Count() > 0)
            {
                log.LogInformation($"{freeSlots.Count()} available");
                foreach (var item in freeSlots)
                {
                    log.LogDebug($"SLOT: (status={item.Status}) {item.StartDateTime.ToString("f")}");
                }
                await Notify(freeSlots);

            }
            else
            {


                var firstSlot = slots.OrderBy(s => s.StartDateTime).FirstOrDefault();
                var lastSlot = slots.OrderByDescending(s => s.StartDateTime).First();
                log.LogInformation($"There are no free slots in the period {firstSlot.StartDateTime.ToShortDateString()} to {lastSlot.EndDateTime.ToShortDateString()}");
                await Notify();
            }


            lastSlotCurrently = slots.OrderByDescending(s => s.StartDateTime).First().EndDateTime;
            if (lastSlotCurrently > lastSlotPreviously)
            {
                log.LogInformation($"Date of the last slot has changed.  It is now: {lastSlotCurrently.ToShortDateString()}");
                await Notify(lastSlotPreviously, lastSlotCurrently);
            }
            await SetPreviousSlotDate(lastSlotCurrently);
            await SaveSlotDateUpdateHistory(lastSlotCurrently);
            return;
        }

        private async Task<DateTime> GetPreviousSlotDate()
        {
            CloudBlobClient blobClient;
            CloudBlobContainer inboundContainer;

            try
            {

                var storageAccount = CloudStorageAccount.Parse(_storageConnectionString);
                blobClient = storageAccount.CreateCloudBlobClient();
                inboundContainer = blobClient.GetContainerReference(_monitoringContext.MonitoringContainerName);
                var dateBlob = inboundContainer.GetBlockBlobReference("dateBlob.txt");
                string blobDate = await dateBlob.DownloadTextAsync();
                DateTime dateValue;
                if (DateTime.TryParse(blobDate, out dateValue))
                    return dateValue;
                else
                {
                    return DateTime.MinValue;
                }

            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
        private async Task SetPreviousSlotDate(DateTime latestSlotDate)
        {
            CloudBlobClient blobClient;
            CloudBlobContainer inboundContainer;

            try
            {
                var storageAccount = CloudStorageAccount.Parse(_storageConnectionString);
                blobClient = storageAccount.CreateCloudBlobClient();
                inboundContainer = blobClient.GetContainerReference(_monitoringContext.MonitoringContainerName);
                var dateBlob = inboundContainer.GetBlockBlobReference("dateBlob.txt");
                await dateBlob.UploadTextAsync(latestSlotDate.ToString("o"));
            }
            catch (Exception e)
            {
                throw new Exception($"Exception loadeding last slot date from storage {e.Message}");
            }
        }
        private async Task<string> CheckForSlots()
        {

            CloudBlobClient blobClient;
            CloudBlobContainer inboundContainer;
            string reqbodyBlobContents = "";
            string[] reqhdrBlobContents = null;
            try
            {
                var storageAccount = CloudStorageAccount.Parse(_storageConnectionString);
                blobClient = storageAccount.CreateCloudBlobClient();
                inboundContainer = blobClient.GetContainerReference(_monitoringContext.MonitoringContainerName);
                var reqhdrBlob = inboundContainer.GetBlockBlobReference(_monitoringContext.RequestHeaderFileName);
                var reqhdrBlobContentsString = await reqhdrBlob.DownloadTextAsync();
                string[] stringSeparators = new string[] { "\r\n" };
                reqhdrBlobContents = reqhdrBlobContentsString.Split(stringSeparators, StringSplitOptions.None);

                var reqbodyBlob = inboundContainer.GetBlockBlobReference(_monitoringContext.RequestBodyFileName);
                reqbodyBlobContents = await reqbodyBlob.DownloadTextAsync();
                var jsonBody = JObject.Parse(reqbodyBlobContents);
                jsonBody["data"]["start_date"] = DateTime.UtcNow.ToString("o");
                jsonBody["data"]["end_date"] = DateTime.UtcNow.AddDays(15).ToString("o");
                reqbodyBlobContents = jsonBody.ToString();
            }
            catch (Exception e)
            {
                throw new Exception($"Error processing blob storage {e.Message}", e);
            }
            string responseBody = null;
            try
            {
                
                var url = $"https://groceries.asda.com/api/v3/slot/view";
                HttpResponseMessage response;
                var clientHandler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                using (var client = new HttpClient(clientHandler))
                {
                    client.DefaultRequestHeaders.Clear();
                    var data = new StringContent(reqbodyBlobContents, Encoding.UTF8, "application/json");
                    foreach (var line in reqhdrBlobContents)
                    {
                        var header = line.Split(':');
                        var name = header[0].Trim();
                        var value = header[1].Trim();
                        if (!name.ToLower().Contains("content-"))
                        {
                            client.DefaultRequestHeaders.Add(name, value);
                        }
                    }
                    data.Headers.ContentLength = reqbodyBlobContents.Length;
                    data.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    response = await client.PostAsync(url, data);
                    if (response.IsSuccessStatusCode)
                    {
                        responseBody = await response.Content.ReadAsStringAsync();

                    }
                    else
                    {
                        throw new Exception($"That didnt work.  Calling slots api {url} Response:{response.StatusCode.ToString()}");
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Error calling grocery service {e.Message}", e);
            }
            return responseBody;
        }
        private static List<DeliverySlot> ParseSlots(string jsonToParse)
        {
            JObject jsonContent;
            JToken slotDays;
            List<DeliverySlot> deliverySlots = new List<DeliverySlot>();
            try
            {

                jsonContent = JObject.Parse(jsonToParse);
                slotDays = jsonContent["data"]["slot_days"];
            }
            catch (Exception e)
            {
                throw new Exception($"Error parsing json response {e.Message}.  See inner exception for details", e);
            }


            foreach (var slotDay in slotDays.Children())
            {
                foreach (var day in slotDay.Children())
                {

                    foreach (var slots in day.Children())
                    {

                        foreach (var slot in slots.Children())
                        {

                            var slotInfo = slot["slot_info"];

                            DeliverySlot ds = new DeliverySlot();
                            ds.SlotId = slotInfo["slot_id"].ToString();

                            DateTime dateValue;
                            if (DateTime.TryParse(slotInfo["start_time"].ToString(), out dateValue))
                                ds.StartDateTime = dateValue;
                            else
                            {
                                throw new Exception($"Slot {ds.SlotId} has an invalid start date");
                            }

                            if (DateTime.TryParse(slotInfo["end_time"].ToString(), out dateValue))
                                ds.EndDateTime = dateValue;
                            else
                            {
                                throw new Exception($"Slot {ds.SlotId} has an invalid end date");
                            }


                            ds.Status = slotInfo["status"].ToString();
                            deliverySlots.Add(ds);

                        }



                    }


                }


            }
            return deliverySlots;

        }
        private async Task Notify(IEnumerable<DeliverySlot> freeSlots = null)
        {
            bool slotsAvailable = false;
            if ((!(freeSlots == null)) && (freeSlots.Count() > 0))
            {
                slotsAvailable = true;
            }
            if (!slotsAvailable && (_monitoringContext.NotifyUnavailability.ToLower() == "false"))
            {
                return;
            }
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("ASDA Slot Checker", "nick@nikkh.net"));
            message.To.Add(new MailboxAddress("Nick Hill", "nhill@microsoft.com"));
            message.To.Add(new MailboxAddress("Nikki Chatwin", "nikki.chatwin@hotmail.co.uk"));

            if (slotsAvailable)
            {
                message.Subject = "ASDA Slots: There are free slots available at ASDA Groceries!";
                string body = $"There are free slots as of {DateTime.UtcNow.ToLocalTime().ToString("F")}:{Environment.NewLine}";
                foreach (var slot in freeSlots)
                {
                    body += $"{slot.StartDateTime.ToString("F")}, Status = {slot.Status}{Environment.NewLine}";
                }

                message.Body = new TextPart("plain")
                {
                    Text = $"{body}"
                };
            }
            else
            {
                message.Subject = "ASDA Slots: None Available :-(";
                string body = $"There are no free slots as of {DateTime.UtcNow.ToLocalTime().ToShortDateString()} at {DateTime.UtcNow.ToLocalTime().ToShortTimeString()}{Environment.NewLine}";
                message.Body = new TextPart("plain")
                {
                    Text = $"{body}"
                };
            }


            using (var client = new SmtpClient())
            {
                await client.ConnectAsync("smtp.office365.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("nick@nikkh.net", _mailPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        private async Task Notify(DateTime previousLastSlotDate, DateTime currentLastSlotDate)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("ASDA Slot Checker", "nick@nikkh.net"));
            message.To.Add(new MailboxAddress("Nick Hill", "nhill@microsoft.com"));
            message.To.Add(new MailboxAddress("Nikki Chatwin", "nikki.chatwin@hotmail.co.uk"));
            message.Subject = $"Slots have been released up to {currentLastSlotDate.ToShortDateString()}";
            string body = $"The horizon for which slots have been published has been extended to {currentLastSlotDate.ToString("F")}{Environment.NewLine}";
            body += $"This change was detected at {DateTime.UtcNow.ToLocalTime().ToShortTimeString()}";
            message.Body = new TextPart("plain")
            {
                Text = $"{body}"
            };
            using (var client = new SmtpClient())
            {
                await client.ConnectAsync("smtp.office365.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("nick@nikkh.net", _mailPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }
        private async Task SaveSlotDateUpdateHistory(DateTime latestSlotDate)
        {
            CloudBlobClient blobClient;
            CloudBlobContainer inboundContainer;

            try
            {
                var storageAccount = CloudStorageAccount.Parse(_storageConnectionString);
                blobClient = storageAccount.CreateCloudBlobClient();
                inboundContainer = blobClient.GetContainerReference(_monitoringContext.MonitoringContainerName);
                var historyBlob = inboundContainer.GetAppendBlobReference("slotDateHistory.txt");
                try
                {
                    await historyBlob.CreateOrReplaceAsync(AccessCondition.GenerateIfNotExistsCondition(), new BlobRequestOptions() { RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(1), 10) }, null);
                }
                catch (Exception) 
                {
                    ;
                }
                await historyBlob.AppendTextAsync($"Date={DateTime.UtcNow.ToLongDateString()}, Time={DateTime.UtcNow.ToLongTimeString()}, Date of Latest Slot={latestSlotDate.ToString("o")}{Environment.NewLine}");
            }
            catch (Exception e)
            {
                throw new Exception($"Exception saving slot date history to storage {e.Message}");
            }
        }
    }
}
