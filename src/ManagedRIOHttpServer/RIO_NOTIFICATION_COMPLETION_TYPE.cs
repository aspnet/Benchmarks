namespace ManagedRIOHttpServer
{
    public enum RIO_NOTIFICATION_COMPLETION_TYPE : int
    {
        POLLING = 0,
        EVENT_COMPLETION = 1,
        IOCP_COMPLETION = 2
    }
}
