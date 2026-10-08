// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;

namespace Crank.PerfLabExporter.Publishing
{
    public enum StorageAuthenticationMode
    {
        Default,
        ManagedIdentity,
        Certificate
    }

    public sealed class StorageAuthenticationOptions
    {
        public StorageAuthenticationMode Mode { get; init; }
        public string? ManagedIdentityClientId { get; init; }
        public string? ManagedIdentityClientIdEnvironmentVariable { get; init; }
        public string? TenantId { get; init; }
        public string? TenantIdEnvironmentVariable { get; init; }
        public string? ClientId { get; init; }
        public string? ClientIdEnvironmentVariable { get; init; }
        public string? CertificatePath { get; init; }
        public string? CertificatePathEnvironmentVariable { get; init; }
        public string? CertificateBase64EnvironmentVariable { get; init; }
        public string? CertificatePasswordEnvironmentVariable { get; init; }
    }

    public static class AzureCredentialFactory
    {
        public static TokenCredential Create(
            StorageAuthenticationOptions options)
        {
            return Create(options.Mode, options);
        }

        internal static TokenCredential Create(
            StorageAuthenticationMode mode,
            StorageAuthenticationOptions options)
        {
            var hasManagedIdentity =
                HasValue(options.ManagedIdentityClientId) ||
                HasValue(
                    options.ManagedIdentityClientIdEnvironmentVariable);
            var hasCertificate =
                HasValue(options.TenantId) ||
                HasValue(options.TenantIdEnvironmentVariable) ||
                HasValue(options.ClientId) ||
                HasValue(options.ClientIdEnvironmentVariable) ||
                HasValue(options.CertificatePath) ||
                HasValue(options.CertificatePathEnvironmentVariable) ||
                HasValue(options.CertificateBase64EnvironmentVariable);
            if ((mode == StorageAuthenticationMode.Default &&
                 (hasManagedIdentity || hasCertificate)) ||
                (mode == StorageAuthenticationMode.ManagedIdentity &&
                 hasCertificate) ||
                (mode == StorageAuthenticationMode.Certificate &&
                 hasManagedIdentity))
            {
                throw new ArgumentException(
                    "Credential options do not match the selected authentication mode.");
            }

            return mode switch
            {
                StorageAuthenticationMode.Default =>
                    new DefaultAzureCredential(),
                StorageAuthenticationMode.ManagedIdentity =>
                    new ManagedIdentityCredential(
                        Resolve(
                            options.ManagedIdentityClientId,
                            options.ManagedIdentityClientIdEnvironmentVariable,
                            required: false) is { } clientId
                                ? ManagedIdentityId.FromUserAssignedClientId(
                                    clientId)
                                : ManagedIdentityId.SystemAssigned),
                StorageAuthenticationMode.Certificate =>
                    CreateCertificate(options),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(options),
                    mode,
                    "Unknown authentication mode.")
            };
        }

        private static TokenCredential CreateCertificate(
            StorageAuthenticationOptions options)
        {
            var tenantId = Resolve(
                options.TenantId,
                options.TenantIdEnvironmentVariable,
                required: true)!;
            var clientId = Resolve(
                options.ClientId,
                options.ClientIdEnvironmentVariable,
                required: true)!;
            var certificatePath = Resolve(
                options.CertificatePath,
                options.CertificatePathEnvironmentVariable,
                required: false);
            var encodedCertificate = ReadEnvironment(
                options.CertificateBase64EnvironmentVariable,
                required: false);
            if ((certificatePath is null) == (encodedCertificate is null))
            {
                throw new ArgumentException(
                    "Certificate authentication requires exactly one certificate path or base64 certificate environment variable.");
            }

            var password = ReadEnvironment(
                options.CertificatePasswordEnvironmentVariable,
                required: false);
            X509Certificate2 certificate;
            if (certificatePath is not null)
            {
                var fullPath = Path.GetFullPath(certificatePath);
                certificate = Path.GetExtension(fullPath)
                    .Equals(".pem", StringComparison.OrdinalIgnoreCase)
                        ? string.IsNullOrEmpty(password)
                            ? X509Certificate2.CreateFromPemFile(fullPath)
                            : X509Certificate2.CreateFromEncryptedPemFile(
                                fullPath,
                                password)
                        : X509CertificateLoader.LoadPkcs12FromFile(
                            fullPath,
                            password,
                            X509KeyStorageFlags.EphemeralKeySet);
            }
            else
            {
                try
                {
                    certificate = X509CertificateLoader.LoadPkcs12(
                        Convert.FromBase64String(encodedCertificate!),
                        password,
                        X509KeyStorageFlags.EphemeralKeySet);
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException(
                        "The certificate environment variable is not valid base64.",
                        exception);
                }
            }

            return new ClientCertificateCredential(
                tenantId,
                clientId,
                certificate,
                new ClientCertificateCredentialOptions
                {
                    SendCertificateChain = true
                });
        }

        private static string? Resolve(
            string? directValue,
            string? environmentVariable,
            bool required)
        {
            if (!string.IsNullOrWhiteSpace(directValue) &&
                !string.IsNullOrWhiteSpace(environmentVariable))
            {
                throw new ArgumentException(
                    "Specify either a direct credential value or an environment variable, not both.");
            }

            var value = !string.IsNullOrWhiteSpace(directValue)
                ? directValue
                : ReadEnvironment(environmentVariable, required);
            if (required && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A required certificate credential value is missing.");
            }

            return value;
        }

        private static string? ReadEnvironment(
            string? name,
            bool required)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var value = Environment.GetEnvironmentVariable(name);
            if (required && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"Credential environment variable '{name}' is not set.");
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static bool HasValue(string? value) =>
            !string.IsNullOrWhiteSpace(value);
    }
}
