using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace AzDoConsumer
{
    public class JobPayload
    {
        public string[] Args { get; set; }

        public static JobPayload Deserialize(byte[] data)
        {
            var serializer = new DataContractSerializer(typeof(JobPayload));
            using (var stream = new MemoryStream(data))
            using (var reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                return (JobPayload)serializer.ReadObject(reader);
            }
        }
    }
}
