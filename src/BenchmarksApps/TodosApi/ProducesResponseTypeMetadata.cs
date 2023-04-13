using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Http;

internal sealed class ProducesResponseTypeMetadata : IProducesResponseTypeMetadata
{
    private readonly IEnumerable<string> _contentTypes;

    public ProducesResponseTypeMetadata(Type type, int statusCode, string contentType, params string[] additionalContentTypes)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        Type = type ?? throw new ArgumentNullException(nameof(type));
        StatusCode = statusCode;

        MediaTypeHeaderValue.Parse(contentType);
        for (var i = 0; i < additionalContentTypes.Length; i++)
        {
            MediaTypeHeaderValue.Parse(additionalContentTypes[i]);
        }

        _contentTypes = GetContentTypes(contentType, additionalContentTypes);
    }

    /// <summary>
    /// Gets or sets the type of the value returned by an action.
    /// </summary>
    public Type? Type { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code of the response.
    /// </summary>
    public int StatusCode { get; set; }

    public IEnumerable<string> ContentTypes => _contentTypes;

    private static List<string> GetContentTypes(string contentType, string[] additionalContentTypes)
    {
        var contentTypes = new List<string>(additionalContentTypes.Length + 1);
        ValidateContentType(contentType);
        contentTypes.Add(contentType);
        foreach (var type in additionalContentTypes)
        {
            ValidateContentType(type);
            contentTypes.Add(type);
        }

        return contentTypes;

        static void ValidateContentType(string type)
        {
            if (type.Contains('*', StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Could not parse '{type}'. Content types with wildcards are not supported.");
            }
        }
    }
}