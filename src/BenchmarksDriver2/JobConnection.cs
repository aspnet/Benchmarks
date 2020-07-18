﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Benchmarks.ServerJob;
using BenchmarksDriver.Ignore;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace BenchmarksDriver
{
    public class JobConnection
    {
        static readonly HttpClient _httpClient;
        static readonly HttpClientHandler _httpClientHandler;

        static List<string> _temporaryFolders = new List<string>();

        private static string _filecache = null;

        // The uri of the server
        private readonly Uri _serverUri;

        // The uri of the /jobs endpoint on the server
        private readonly Uri _serverJobsUri;
        private string _serverJobUri;
        private bool _keepAlive;
        private DateTime _runningUtc;
        private string _jobName;

        private int _outputCursor;
        private int _buildCursor;

        static JobConnection()
        {
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);
        }

        public JobConnection(ServerJob definition, Uri serverUri)
        {
            Job = definition;
            _serverUri = serverUri;
            _serverJobsUri = new Uri(_serverUri, "/jobs");
        }

        public string ServerJobUri => _serverJobUri;

        public ServerJob Job { get; private set; }

        public async Task<string> StartAsync(
            string jobName,
            CommandOption _outputArchiveOption,
            CommandOption _buildArchiveOption
            )
        {
            _jobName = jobName;

            var content = JsonConvert.SerializeObject(Job);

            Log.Write($"Starting job '{_jobName}' ...");

            Log.Verbose($"POST {_serverJobsUri} {content} ...");

            var response = await _httpClient.PostAsync(_serverJobsUri, new StringContent(content, Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");

            response.EnsureSuccessStatusCode();

            _serverJobUri = new Uri(_serverUri, response.Headers.Location).ToString();

            Log.Write($"Fetching job: {_serverJobUri}");

            // When a job is submitted it has the state New
            // Waiting for the job to be selected (Initializing), then upload custom files and send the start

            while (true)
            {
                Log.Verbose($"GET {_serverJobUri} ...");
                response = await _httpClient.GetAsync(_serverJobUri);
                responseContent = await response.Content.ReadAsStringAsync();

                response.EnsureSuccessStatusCode();

                Job = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                #region Ensure the job is valid

                if (Job.ServerVersion < 4)
                {
                    throw new Exception($"Invalid server version ({Job.ServerVersion}), please update your server to match this driver version.");
                }

                if (!Job.Hardware.HasValue)
                {
                    throw new InvalidOperationException("Server is required to set ServerJob.Hardware.");
                }

                if (String.IsNullOrWhiteSpace(Job.HardwareVersion))
                {
                    throw new InvalidOperationException("Server is required to set ServerJob.HardwareVersion.");
                }

                if (!Job.OperatingSystem.HasValue)
                {
                    throw new InvalidOperationException("Server is required to set ServerJob.OperatingSystem.");
                }

                #endregion

                if (Job.State == ServerState.Initializing)
                {
                    Log.Write($"Job has been selected by the server ...");

                    StartKeepAlive();
                    
                    // Uploading source code
                    if (!String.IsNullOrEmpty(Job.Source.LocalFolder))
                    {
                        // Zipping the folder
                        var tempFilename = Path.GetTempFileName();
                        File.Delete(tempFilename);

                        Log.Write("Zipping the source folder in " + tempFilename);

                        var sourceDir = Job.Source.LocalFolder;

                        if (!File.Exists(Path.Combine(sourceDir, ".gitignore")))
                        {
                            ZipFile.CreateFromDirectory(sourceDir, tempFilename);
                        }
                        else
                        {
                            Log.Verbose(".gitignore file found");
                            DoCreateFromDirectory(sourceDir, tempFilename);
                        }

                        var result = await UploadFileAsync(tempFilename, Job, _serverJobUri + "/source");

                        File.Delete(tempFilename);

                        if (result != 0)
                        {
                            throw new Exception("Error while uploading source files");
                        }
                    }

                    // Upload custom package contents
                    if (_outputArchiveOption.HasValue())
                    {
                        foreach (var outputArchiveValue in _outputArchiveOption.Values)
                        {
                            var outputFileSegments = outputArchiveValue.Split(';', 2, StringSplitOptions.RemoveEmptyEntries);

                            string localArchiveFilename = outputFileSegments[0];

                            var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                            if (Directory.Exists(tempFolder))
                            {
                                Directory.Delete(tempFolder, true);
                            }

                            Directory.CreateDirectory(tempFolder);

                            _temporaryFolders.Add(tempFolder);

                            // Download the archive, while pinging the server to keep the job alive
                            if (outputArchiveValue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                localArchiveFilename = await DownloadTemporaryFileAsync(localArchiveFilename, _serverJobUri);
                            }

                            ZipFile.ExtractToDirectory(localArchiveFilename, tempFolder);

                            if (outputFileSegments.Length > 1)
                            {
                                Job.Options.OutputFiles.Add(Path.Combine(tempFolder, "*.*") + ";" + outputFileSegments[1]);
                            }
                            else
                            {
                                Job.Options.OutputFiles.Add(Path.Combine(tempFolder, "*.*"));
                            }
                        }
                    }

                    // Upload custom build package contents
                    if (_buildArchiveOption.HasValue())
                    {
                        foreach (var buildArchiveValue in _buildArchiveOption.Values)
                        {
                            var buildFileSegments = buildArchiveValue.Split(';', 2, StringSplitOptions.RemoveEmptyEntries);

                            string localArchiveFilename = buildFileSegments[0];

                            var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                            if (Directory.Exists(tempFolder))
                            {
                                Directory.Delete(tempFolder, true);
                            }

                            Directory.CreateDirectory(tempFolder);

                            _temporaryFolders.Add(tempFolder);

                            // Download the archive, while pinging the server to keep the job alive
                            if (buildArchiveValue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                localArchiveFilename = await DownloadTemporaryFileAsync(localArchiveFilename, _serverJobUri);
                            }

                            ZipFile.ExtractToDirectory(localArchiveFilename, tempFolder);

                            if (buildFileSegments.Length > 1)
                            {
                                Job.Options.BuildFiles.Add(Path.Combine(tempFolder, "*.*") + ";" + buildFileSegments[1]);
                            }
                            else
                            {
                                Job.Options.BuildFiles.Add(Path.Combine(tempFolder, "*.*"));
                            }
                        }
                    }

                    // Uploading build files
                    if (Job.Options.BuildFiles.Any())
                    {
                        foreach (var buildFileValue in Job.Options.BuildFiles)
                        {
                            var buildFileSegments = buildFileValue.Split(';', 2, StringSplitOptions.RemoveEmptyEntries);

                            var shouldSearchRecursively = buildFileSegments[0].Contains("*.*");

                            foreach (var resolvedFile in Directory.GetFiles(Path.GetDirectoryName(buildFileSegments[0]), Path.GetFileName(buildFileSegments[0]), shouldSearchRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                            {
                                var resolvedFileWithDestination = resolvedFile;

                                if (buildFileSegments.Length > 1)
                                {
                                    resolvedFileWithDestination += ";" + buildFileSegments[1] + Path.GetDirectoryName(resolvedFile).Substring(Path.GetDirectoryName(buildFileSegments[0]).Length) + "/" + Path.GetFileName(resolvedFileWithDestination);
                                }

                                var result = await UploadFileAsync(resolvedFileWithDestination, Job, _serverJobUri + "/build");

                                if (result != 0)
                                {
                                    throw new Exception("Error while uploading build files");
                                }
                            }
                        }
                    }

                    // Uploading attachments
                    if (Job.Options.OutputFiles.Any())
                    {
                        foreach (var outputFileValue in Job.Options.OutputFiles)
                        {
                            var outputFileSegments = outputFileValue.Split(';', 2, StringSplitOptions.RemoveEmptyEntries);

                            var shouldSearchRecursively = outputFileSegments[0].Contains("*.*");

                            foreach (var resolvedFile in Directory.GetFiles(Path.GetDirectoryName(outputFileSegments[0]), Path.GetFileName(outputFileSegments[0]), shouldSearchRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                            {
                                var resolvedFileWithDestination = resolvedFile;

                                if (outputFileSegments.Length > 1)
                                {
                                    resolvedFileWithDestination += ";" + outputFileSegments[1] + Path.GetDirectoryName(resolvedFile).Substring(Path.GetDirectoryName(outputFileSegments[0]).Length) + "/" + Path.GetFileName(resolvedFileWithDestination);
                                }

                                var result = await UploadFileAsync(resolvedFileWithDestination, Job, _serverJobUri + "/attachment");

                                if (result != 0)
                                {
                                    throw new Exception("Error while uploading output files");
                                }
                            }
                        }
                    }

                    response = await _httpClient.PostAsync(_serverJobUri + "/start", new StringContent(""));
                    responseContent = await response.Content.ReadAsStringAsync();
                    Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");
                    response.EnsureSuccessStatusCode();

                    Job = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                    Log.Write($"Job is now building ...");

                    break;
                }
                else
                {
                    await Task.Delay(1000);
                }
            }

            // Tracking the job until it stops

            // TODO: Add a step on the server before build and start, such that start can be as fast as possible
            // "start" => "build"
            // + new start call

            while (true)
            {
                var previousJob = Job;

                Log.Verbose($"GET {_serverJobUri} ...");
                response = await _httpClient.GetAsync(_serverJobUri);
                responseContent = await response.Content.ReadAsStringAsync();

                Log.Verbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new Exception("Job not found");
                }

                Job = JsonConvert.DeserializeObject<ServerJob>(responseContent);


                if (Job.State == ServerState.Running)
                {
                    if (previousJob.State != ServerState.Running)
                    {
                        Log.Write($"Job is running");
                        _runningUtc = DateTime.UtcNow;
                    }

                    return Job.Url;
                }
                else if (Job.State == ServerState.Failed)
                {
                    Log.Write($"Job failed on benchmark server, stopping ...");

                    Log.Write(Job.Error, notime: true, error: true);

                    // Returning will also send a Delete message to the server
                    return null;
                }
                else if (Job.State == ServerState.NotSupported)
                {
                    Log.Write("Server does not support this job configuration.");
                    return null;
                }
                else if (Job.State == ServerState.Stopped)
                {
                    Log.Write($"Job finished");

                    // If there is no ReadyStateText defined, the server will never be in Running state
                    // and we'll reach the Stopped state eventually, but that's a normal behavior.
                    if (Job.WaitForExit)
                    {
                        return Job.Url;
                    }

                    throw new Exception("Job finished unnexpectedly");
                }
                else
                {
                    await Task.Delay(1000);
                }
            }
        }

        /// <summary>
        /// Stops the job on the server without deleting it.
        /// </summary>
        public async Task StopAsync()
        {
            StopKeepAlive();

            Log.Write($"Stopping job '{_jobName}' ...");

            var response = await _httpClient.PostAsync(_serverJobUri + "/stop", new StringContent(""));
            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");
            var jobStoppedUtc = DateTime.UtcNow;

            // Wait for Stop state
            while (true)
            {
                await Task.Delay(1000);

                var state = await GetStateAsync();

                if (state == ServerState.Stopped || state == ServerState.Failed)
                {
                    break;
                }

                if (DateTime.UtcNow - jobStoppedUtc > TimeSpan.FromSeconds(30))
                {
                    // The job needs to be deleted
                    Log.Write($"Server didn't stop the job in the expected time, deleting it ...");

                    break;
                }
            }
        }

        /// <summary>
        /// Deletes the job on the server.
        /// </summary>
        public async Task DeleteAsync()
        {
            Log.Write($"Deleting job '{_jobName}' ...");

            Log.Verbose($"DELETE {_serverJobUri} ...");
            var response = await _httpClient.DeleteAsync(_serverJobUri);
            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Write($@"Server job was not found, it must have been aborted. Possible cause:
                            - Issue while cloning the repository (GitHub unresponsive)
                            - Issue while restoring (MyGet/NuGet unresponsive)
                            - Issue while building
                            - Issue while running (Timeout)"
                );
            }

            response.EnsureSuccessStatusCode();
        }


        /// <summary>
        /// Downloads the whole job including the measurements.
        /// </summary>
        public async Task<bool> TryUpdateJobAsync()
        {
            Log.Verbose($"GET {_serverJobUri} ...");
            var response = await _httpClient.GetAsync(_serverJobUri);
            var responseContent = await response.Content.ReadAsStringAsync();

            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

            if(response.StatusCode == HttpStatusCode.NotFound || String.IsNullOrEmpty(responseContent))
            {
                return false;
            }
            else
            {
                try
                {
                    Job = JsonConvert.DeserializeObject<ServerJob>(responseContent);
                }
                catch
                {
                    Log.Write($"ERROR while deserializing state on {_serverJobUri}");
                    return false;
                }

                return true;
            }
        }

        public async Task ClearMeasurements()
        {
            Log.Verbose($"POST {_serverJobUri}/resetstats ...");
            var response = await _httpClient.PostAsync(_serverJobUri + "/resetstats", new StringContent(""));
            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");

            response.EnsureSuccessStatusCode();
        }

        public async Task FlushMeasurements()
        {
            Log.Verbose($"POST {_serverJobUri}/measurements/flush ...");
            var response = await _httpClient.PostAsync(_serverJobUri + "/measurements/flush", new StringContent(""));
            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Returns the current state of the job.
        /// </summary>
        public async Task<ServerState> GetStateAsync()
        {
            Log.Verbose($"GET {_serverJobUri}/state ...");
            var response = await _httpClient.GetAsync(_serverJobUri + "/state");
            var responseContent = await response.Content.ReadAsStringAsync();

            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

            if (response.StatusCode == HttpStatusCode.NotFound || String.IsNullOrEmpty(responseContent))
            {
                return ServerState.Failed;
            }
            else
            {
                try
                {
                    return Enum.Parse<ServerState>(responseContent);
                }
                catch
                {
                    Log.Write($"ERROR while reading state on {_serverJobUri}");
                    return ServerState.Failed;
                }
            }
        }

        /// <summary>
        /// Starts a thread that keeps the job alive on the server.
        /// </summary>
        public void StartKeepAlive()
        {
            if (_keepAlive)
            {
                return;
            }

            _keepAlive = true;

            Task.Run(async () =>
            {
                while (_keepAlive)
                {
                    try
                    {
                        // Ping server job to keep it alive
                        Log.Verbose($"GET {_serverJobUri}/touch ...");
                        var response = await _httpClient.GetAsync(_serverJobUri + "/touch");

                        // Detect if the job has timed out. This doesn't account for any other service
                        if (Job.Timeout > 0 && DateTime.UtcNow - _runningUtc > TimeSpan.FromSeconds(Job.Timeout))
                        {
                            Log.Write($"Job has timed out, stopping ...");
                            await StopAsync();
                        }
                    }
                    catch
                    {
                        Log.Write($"Could not ping the server, retrying ...");
                    }
                    finally
                    {
                        await Task.Delay(2000);
                    }
                }
            });

            if (Job.Options.DisplayBuild)
            {
                Task.Run(async () =>
                {
                    while (_keepAlive)
                    {
                        try
                        {
                            Log.DisplayOutput(await StreamBuildLogAsync());
                        }
                        finally
                        {
                            await Task.Delay(500);
                        }
                    }
                });
            }

            if (Job.Options.DisplayOutput)
            {
                Task.Run(async () =>
                {
                    while (_keepAlive)
                    {
                        try
                        {
                            Log.DisplayOutput(await StreamOutputAsync());
                        }
                        finally
                        {
                            await Task.Delay(500);
                        }
                    }
                });
            }
        }

        public void StopKeepAlive()
        {
            _keepAlive = false;
        }

        public async Task DownloadAssetsAsync(string dependency)
        {
            // Fetch published folder
            if (Job.Options.Fetch)
            {
                try
                {
                    var fetchDestination = Job.Options.FetchOutput;

                    if (String.IsNullOrEmpty(fetchDestination) || !fetchDestination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        // If it does not end with a *.zip then we add a DATE.zip to it
                        if (String.IsNullOrEmpty(fetchDestination))
                        {
                            fetchDestination = dependency;
                        }

                        fetchDestination = fetchDestination + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + ".zip";
                    }

                    Log.Write($"Creating published assets '{fetchDestination}' ...");

                    await FetchAsync(fetchDestination);
                }
                catch (Exception e)
                {
                    Log.Write($"Error while fetching published assets for '{dependency}'");
                    Log.Verbose(e.Message);
                }
            }

            // Download individual files
            if (Job.Options.DownloadFiles != null && Job.Options.DownloadFiles.Any())
            {
                foreach (var file in Job.Options.DownloadFiles)
                {
                    Log.Write($"Downloading file '{file}' for '{dependency}'");

                    try
                    {
                        await DownloadFileAsync(file);
                    }
                    catch (Exception e)
                    {
                        Log.Write($"Error while downloading file {file}, skipping ...");
                        Log.Verbose(e.Message);
                        continue;
                    }
                }
            }
        }
        private static void DoCreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName)
        {
            sourceDirectoryName = Path.GetFullPath(sourceDirectoryName);

            // We ensure the name ends with '\' or '/'
            if (!sourceDirectoryName.EndsWith(Path.AltDirectorySeparatorChar))
            {
                sourceDirectoryName += Path.AltDirectorySeparatorChar;
            }

            destinationArchiveFileName = Path.GetFullPath(destinationArchiveFileName);

            var di = new DirectoryInfo(sourceDirectoryName);

            using (var archive = ZipFile.Open(destinationArchiveFileName, ZipArchiveMode.Create))
            {
                var basePath = di.FullName;

                var ignoreFile = IgnoreFile.Parse(Path.Combine(sourceDirectoryName, ".gitignore"));

                foreach (var gitFile in ignoreFile.ListDirectory(sourceDirectoryName))
                {
                    var localPath = gitFile.Path.Substring(sourceDirectoryName.Length);
                    Log.Verbose($"Adding {localPath}");
                    var entry = archive.CreateEntryFromFile(gitFile.Path, localPath);
                }
            }
        }

        private static async Task<int> UploadFileAsync(string filename, ServerJob serverJob, string uri)
        {
            Log.Write($"Uploading {filename} to {uri}");

            try
            {
                var outputFileSegments = filename.Split(';');
                var uploadFilename = outputFileSegments[0];

                if (!File.Exists(uploadFilename))
                {
                    Console.WriteLine($"File '{uploadFilename}' could not be loaded.");
                    return 8;
                }

                var destinationFilename = outputFileSegments.Length > 1
                    ? outputFileSegments[1]
                    : Path.GetFileName(uploadFilename);

                using (var request = new HttpRequestMessage(HttpMethod.Post, uri))
                {
                    var fileContent = uploadFilename.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new StreamContent(await _httpClient.GetStreamAsync(uploadFilename))
                        : new StreamContent(new FileStream(uploadFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.Asynchronous | FileOptions.SequentialScan));

                    using (fileContent)
                    {
                        request.Content = fileContent;
                        request.Headers.Add("id", serverJob.Id.ToString());
                        request.Headers.Add("destinationFilename", destinationFilename);

                        await _httpClient.SendAsync(request);
                    }
                }
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"An error occurred while uploading a file.", e);
            }

            return 0;
        }

        private static async Task<string> DownloadTemporaryFileAsync(string uri, string serverJobUri)
        {
            if (_filecache == null)
            {
                _filecache = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            }

            Directory.CreateDirectory(_filecache);

            _temporaryFolders.Add(_filecache);

            var filehashname = Path.Combine(_filecache, uri.GetHashCode().ToString());

            if (!File.Exists(filehashname))
            {
                await _httpClient.DownloadFileAsync(uri, serverJobUri, filehashname);
            }

            return filehashname;
        }

        private static void CleanTemporaryFiles()
        {
            foreach (var temporaryFolder in _temporaryFolders)
            {
                if (temporaryFolder != null && Directory.Exists(temporaryFolder))
                {
                    Directory.Delete(temporaryFolder, true);
                }
            }
        }

        public async Task FetchAsync(string fetchDestination)
        {
            var uri = _serverJobUri + "/fetch";
            await File.WriteAllBytesAsync(fetchDestination, await _httpClient.GetByteArrayAsync(uri));
        }

        public async Task<string> DownloadBuildLog()
        {
            var uri = _serverJobUri + "/buildlog";

            return await _httpClient.GetStringAsync(uri);
        }

        public async Task<string> StreamOutputAsync()
        {
            var uri = _serverJobUri + "/output/" + _outputCursor;

            var jsonLines = await _httpClient.GetStringAsync(uri);
            var lines = JsonConvert.DeserializeObject<string[]>(jsonLines);

            _outputCursor += lines.Length;

            using (var sw = new StringWriter())
            {
                foreach (var line in lines)
                {
                    sw.WriteLine($"[{_jobName}] {line}");
                }

                return sw.ToString();
            }
        }

        public async Task<string> StreamBuildLogAsync()
        {
            var uri = _serverJobUri + "/buildlog/" + _buildCursor;

            var jsonLines = await _httpClient.GetStringAsync(uri);
            var lines = JsonConvert.DeserializeObject<string[]>(jsonLines);

            _buildCursor += lines.Length;

            using (var sw = new StringWriter())
            {
                foreach (var line in lines)
                {
                    sw.WriteLine($"[{_jobName}] {line}");
                }

                return sw.ToString();
            }
        }

        public async Task<string> DownloadOutput()
        {
            var uri = _serverJobUri + "/output";

            return await _httpClient.GetStringAsync(uri);
        }

        public async Task DownloadFileAsync(string file)
        {
            var uri = _serverJobUri + "/download?path=" + HttpUtility.UrlEncode(file);
            Log.Verbose("GET " + uri);

            var filename = file;
            var counter = 1;
            while (File.Exists(filename))
            {
                filename = Path.GetFileNameWithoutExtension(file) + counter++ + Path.GetExtension(file);
            }

            await _httpClient.DownloadFileAsync(uri, _serverJobUri, filename);
        }

        public async Task DownloadDotnetTrace(string traceDestination)
        {
            var uri = _serverJobUri + "/trace";
            var response = await _httpClient.PostAsync(uri, new StringContent(""));
            response.EnsureSuccessStatusCode();

            while (true)
            {
                var state = await GetStateAsync();

                if (state == ServerState.Stopped || state == ServerState.Failed)
                {
                    Log.Write($"Can't download the trace. The job was forcibly stopped by the server.");
                    return;
                }

                if (state == ServerState.TraceCollecting)
                {
                    // Server is collecting the trace
                }
                else
                {
                    break;
                }

                await Task.Delay(1000);
            }

            await _httpClient.DownloadFileAsync(uri, _serverJobUri, traceDestination);
        }

        public async Task<Dictionary<string, object>> GetInfoAsync()
        {
            var uri = new Uri(_serverUri, "/info");
            var response = await _httpClient.GetStringAsync(uri);

            return JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
        }

        /// <summary>
        /// Returns the list of active jobs.
        /// </summary>
        public async Task<IEnumerable<JobView>> GetQueueAsync()
        {
            Log.Verbose($"GET {_serverJobsUri} ...");
            var response = await _httpClient.GetAsync(_serverJobsUri);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();

            Log.Verbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

            return JsonConvert.DeserializeObject<JobView[]>(responseContent);
        }
    }
}
