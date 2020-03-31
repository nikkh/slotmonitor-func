using System;
using System.Collections.Generic;
using System.Text;

namespace slotmonitor_func.Contexts
{
    public class MonitoringContext
    {
        public string MonitoringContainerName { get; set; }
        public string RequestHeaderFileName { get; set; }
        public string RequestBodyFileName { get; set; }
        public string NotifyUnavailability { get; set; }
    }
}
