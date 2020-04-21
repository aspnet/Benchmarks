using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace AzDoConsumer
{
    public class DevopsMessage
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly ServiceBusReceivedMessage _message;

        public DevopsMessage(ServiceBusReceivedMessage message)
        {
            _message = message;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + AuthToken)));
        }

        public string PlanUrl => (string)_message.Properties["PlanUrl"];
        public string ProjectId => (string)_message.Properties["ProjectId"];
        public string HubName => (string)_message.Properties["HubName"];
        public string PlanId => (string)_message.Properties["PlanId"];
        public string JobId => (string)_message.Properties["JobId"];
        public string TimelineId => (string)_message.Properties["TimelineId"];
        public string TaskInstanceName => (string)_message.Properties["TaskInstanceName"];
        public string TaskInstanceId => (string)_message.Properties["TaskInstanceId"];
        public string AuthToken => (string)_message.Properties["AuthToken"];

        public Task SendTaskStartedEventAsync()
        {
            var taskStartedEventUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/events?api-version=2.0-preview.1";

            var body = new
            {
                name = "TaskStarted",
                taskId = TaskInstanceId,
                jobId = JobId
            };

            var requestBody = JsonSerializer.Serialize(body);

            return PostDataAsync(taskStartedEventUrl, requestBody);
        }

        private Task<HttpResponseMessage> PostDataAsync(string url, string requestBody)
        {
            var buffer = Encoding.UTF8.GetBytes(requestBody);
            var byteContent = new ByteArrayContent(buffer);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return _httpClient.PostAsync(new Uri(url), byteContent);
        }

        public Task SendTaskCompletedEventAsync(bool succeeded)
        {
            const string taskCompletedEventUrl = "{0}/{1}/_apis/distributedtask/hubs/{2}/plans/{3}/events?api-version=2.0-preview.1";

            var body = new
            {
                name = "TaskCompleted",
                taskId = TaskInstanceId,
                jobId = JobId,
                result = succeeded ? "succeeded" : "failed",
            };
            
            var requestBody = JsonSerializer.Serialize(body);

            return PostDataAsync(taskCompletedEventUrl, requestBody);
        }

        public Task SendTaskLogFeedsAsync(string[] messages)
        {
            var taskLogFeedsUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/timelines/{TimelineId}/records/{JobId}/feed?api-version=4.1";

            var body = new
            {
                value = messages,
                count = messages.Length
            };

            var requestBody = JsonSerializer.Serialize(body);

            return PostDataAsync(taskLogFeedsUrl, requestBody);
        }
    }
}
