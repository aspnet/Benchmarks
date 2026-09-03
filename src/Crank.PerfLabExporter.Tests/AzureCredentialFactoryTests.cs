// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Crank.PerfLabExporter.Publishing;

namespace Crank.PerfLabExporter.Tests
{
    public class AzureCredentialFactoryTests
    {
        [Fact]
        public void CreatesDefaultAndManagedIdentityCredentials()
        {
            Assert.IsType<DefaultAzureCredential>(
                AzureCredentialFactory.Create(
                    new StorageAuthenticationOptions()));
            Assert.IsType<ManagedIdentityCredential>(
                AzureCredentialFactory.Create(
                    new StorageAuthenticationOptions
                    {
                        Mode = StorageAuthenticationMode.ManagedIdentity
                    }));
        }

        [Fact]
        public void CertificateModeRequiresCertificateIdentity()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                AzureCredentialFactory.Create(
                    new StorageAuthenticationOptions
                    {
                        Mode = StorageAuthenticationMode.Certificate
                    }));

            Assert.Contains("credential value is missing", exception.Message);
        }

        [Fact]
        public void RejectsCredentialsForAnotherMode()
        {
            Assert.Throws<ArgumentException>(() =>
                AzureCredentialFactory.Create(
                    new StorageAuthenticationOptions
                    {
                        TenantId = "tenant"
                    }));
        }

        [Fact]
        public void CreatesCertificateCredentialFromBase64EnvironmentVariable()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var tenantVariable = $"PERFLAB_TENANT_{suffix}";
            var clientVariable = $"PERFLAB_CLIENT_{suffix}";
            var certificateVariable = $"PERFLAB_CERTIFICATE_{suffix}";
            using var rsa = RSA.Create();
            var request = new CertificateRequest(
                "CN=Crank.PerfLabExporter.Tests",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5));

            try
            {
                Environment.SetEnvironmentVariable(
                    tenantVariable,
                    "11111111-1111-1111-1111-111111111111");
                Environment.SetEnvironmentVariable(
                    clientVariable,
                    "22222222-2222-2222-2222-222222222222");
                Environment.SetEnvironmentVariable(
                    certificateVariable,
                    Convert.ToBase64String(
                        certificate.Export(X509ContentType.Pkcs12)));

                Assert.IsType<ClientCertificateCredential>(
                    AzureCredentialFactory.Create(
                        new StorageAuthenticationOptions
                        {
                            Mode = StorageAuthenticationMode.Certificate,
                            TenantIdEnvironmentVariable = tenantVariable,
                            ClientIdEnvironmentVariable = clientVariable,
                            CertificateBase64EnvironmentVariable =
                                certificateVariable
                        }));
            }
            finally
            {
                Environment.SetEnvironmentVariable(tenantVariable, null);
                Environment.SetEnvironmentVariable(clientVariable, null);
                Environment.SetEnvironmentVariable(
                    certificateVariable,
                    null);
            }
        }
    }
}
