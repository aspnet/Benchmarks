namespace Benchmarks.ServerJob
{
    public class Attachment
    {
        public string Filename { get; set; }
        public AttachmentLocation Location { get; set; }
        public byte[] Content { get; set; }
    }
}
