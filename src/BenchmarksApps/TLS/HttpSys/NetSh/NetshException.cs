namespace HttpSys.NetSh
{
    public class NetshException : Exception
    {
        public NetshException(string message) : base(message)
        {
        }
        public NetshException(string message, Exception innerException) : base(message, innerException)
        {
        }
        public NetshException(string message, string command) : base($"{message} Command: {command}")
        {
        }
        public NetshException(string message, string command, Exception innerException) : base($"{message} Command: {command}", innerException)
        {
        }
    }
}
