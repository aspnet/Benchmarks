using System;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Mvc;

namespace BenchmarkServer
{
    public class JobResult
    {
        public JobResult(ServerJob job, IUrlHelper urlHelper)
        {
            Id = job.Id;
            RunId = job.RunId;
            State = job.State.ToString();
            DetailsUrl = urlHelper.ActionLink("GetById", "Jobs", new { Id });
            BuildLogsUrl = urlHelper.ActionLink("BuildLog", "Jobs", new { Id });
            OutputLogsUrl = urlHelper.ActionLink("Output", "Jobs", new { Id });
        }

        public int Id { get; set; }
        public string RunId { get; set; }
        public string State { get; set;}
        public string DetailsUrl { get; set; }
        public string BuildLogsUrl { get; set; }
        public string OutputLogsUrl { get; set; }
    }
}
