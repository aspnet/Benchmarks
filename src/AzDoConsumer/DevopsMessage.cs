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

        public string PlanUrl { get; set; }
        public string ProjectId { get; set; }
        public string HubName { get; set; }
        public string PlanId { get; set; }
        public string JobId { get; set; }
        public string TimelineId { get; set; }
        public string TaskInstanceName { get; set; }
        public string TaskInstanceId { get; set; }
        public string AuthToken { get; set; }

        public DevopsMessage(ServiceBusReceivedMessage message)
        {
            PlanUrl = (string)message.Properties["PlanUrl"];
            ProjectId = (string)message.Properties["ProjectId"];
            HubName = (string)message.Properties["HubName"];
            PlanId = (string)message.Properties["PlanId"];
            JobId = (string)message.Properties["JobId"];
            TimelineId = (string)message.Properties["TimelineId"];
            TaskInstanceName = (string)message.Properties["TaskInstanceName"];
            TaskInstanceId = (string)message.Properties["TaskInstanceId"];
            AuthToken = (string)message.Properties["AuthToken"];

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + AuthToken)));
        }

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

        
        public Task SendTaskCompletedEventAsync(bool succeeded)
        {
            var taskCompletedEventUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/events?api-version=2.0-preview.1";

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

        public Task SendTaskLogFeedsAsync(string message)
        {
            var taskLogFeedsUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/timelines/{TimelineId}/records/{JobId}/feed?api-version=4.1";

            var body = new
            {
                value = new string[] { message },
                count = 1
            };

            var requestBody = JsonSerializer.Serialize(body);

            return PostDataAsync(taskLogFeedsUrl, requestBody);
        }

        public async Task<string> CreateTaskLogAsync()
        {
            var taskLogCreateUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/logs?api-version=4.1";

            var body = new
            {
                path = String.Format(@"logs\{0:D}", TaskInstanceId),
            };

            var requestBody = JsonSerializer.Serialize(body);

            var response = await PostDataAsync(taskLogCreateUrl, requestBody);

            return await response.Content.ReadAsStringAsync();
        }

        public Task AppendToTaskLogAsync(string taskLogId, string message)
        {
            // Append to task log
            // url: {planUri}/{projectId}/_apis/distributedtask/hubs/{hubName}/plans/{planId}/logs/{taskLogId}?api-version=4.1
            // body: log messages stream data

            var appendLogContentUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/logs/{taskLogId}?api-version=4.1";

            var buffer = Encoding.UTF8.GetBytes(message);
            var byteContent = new ByteArrayContent(buffer);
                       

            return PostDataAsync(appendLogContentUrl, byteContent);
        }

        public Task UpdateTaskTimelineRecordAsync(string taskLogObject)
        {
            var updateTimelineUrl = $"{PlanUrl}/{ProjectId}/_apis/distributedtask/hubs/{HubName}/plans/{PlanId}/timelines/{TimelineId}/records?api-version=4.1";

            var timelineRecord = new
            {
                id = TaskInstanceId,
                log = taskLogObject
            };

            var body = new
            {
                value = new[] { timelineRecord },
                count = 1
            };

            var requestBody = JsonSerializer.Serialize(body);

            return PatchDataAsync(updateTimelineUrl, requestBody);
        }

        
        private async Task<HttpResponseMessage> PostDataAsync(string url, HttpContent content)
        {
            var response = await _httpClient.PostAsync(new Uri(url), content);

            response.EnsureSuccessStatusCode();

            return response;
        }

        private async Task<HttpResponseMessage> PostDataAsync(string url, string requestBody)
        {
            var buffer = Encoding.UTF8.GetBytes(requestBody);
            var byteContent = new ByteArrayContent(buffer);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await _httpClient.PostAsync(new Uri(url), byteContent);

            response.EnsureSuccessStatusCode();

            return response;
        }

        private async Task<HttpResponseMessage> PatchDataAsync(string url, string requestBody)
        {
            var buffer = Encoding.UTF8.GetBytes(requestBody);
            var byteContent = new ByteArrayContent(buffer);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await _httpClient.PatchAsync(new Uri(url), byteContent);

            response.EnsureSuccessStatusCode();

            return response;
        }

    }
}
