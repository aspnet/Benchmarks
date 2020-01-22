// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Benchmarks.ServerJob
{
    public enum ServerState
    {
        New, // The job was submitted
        Initializing, // The job is processed, the driver update it or submit attachments
        Waiting, // The job is ready to start, following a POST from the client to /start
        Starting, // The application has been started, the server is waiting for it to be responsive
        Running, // The application is running
        Failed,
        Deleting,
        Deleted,
        Stopping,
        Stopped,
        TraceCollecting,
        TraceCollected,
        NotSupported, // The job is not supported by the server
    }
}
