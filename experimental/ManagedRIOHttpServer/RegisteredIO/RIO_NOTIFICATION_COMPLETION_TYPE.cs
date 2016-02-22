// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace ManagedRIOHttpServer.RegisteredIO
{
    public enum RIO_NOTIFICATION_COMPLETION_TYPE : int
    {
        POLLING = 0,
        EVENT_COMPLETION = 1,
        IOCP_COMPLETION = 2
    }
}
