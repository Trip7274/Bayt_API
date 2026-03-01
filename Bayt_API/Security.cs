using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bayt_API;

public static class Security
{
	public static class Certificates
	{
		static Certificates()
		{
			if (ParsingMethods.IsEnvVarTrue("BAYT_DISABLE_HTTPS")) return;

			string certPath = Path.Combine(ApiConfig.BaseDataPath, "certs", "baytCert.pfx");
			if (!File.Exists(certPath))
			{
				Logs.LogBook.Write(new (StreamId.Info, "HTTPS Initalization", "No HTTPS certificate found. Bayt will generate one."));
				BaytCertificate = MakeBaytCertificate(certPath);
				return;
			}

			X509Certificate2? certificate = null;
			byte iterations = 0;
			while (true)
			{
				iterations++;

				try
				{
					certificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, "");
				}
				catch (Exception e)
				{
					Logs.LogBook.Write(new (StreamId.Error, "HTTPS Initalization", $"Failed to load HTTPS certificate: {e.Message}"));
					break;
				}

				if (iterations > 3)
				{
					Logs.LogBook.Write(new (StreamId.Error, "HTTPS Initalization", "Failed to load HTTPS certificate due to some internal error. Bayt will continue without HTTPS."));
					break;
				}

				var now = DateTime.Now;
				if (now > certificate.NotAfter || now < certificate.NotBefore)
				{
					Logs.LogBook.Write(new (StreamId.Verbose, "HTTPS Initalization", "HTTPS certificate is expired. Bayt will generate a new one."));
					MakeBaytCertificate(certPath);
				}
				else
				{
					Logs.LogBook.Write(new (StreamId.Info, "HTTPS Initalization", "Loaded HTTPS certificate."));
					break;
				}
			}

			if (certificate is not null) BaytCertificate = certificate;
		}

		internal static readonly X509Certificate2? BaytCertificate;
		public static PublicKey? BaytPublicKey => BaytCertificate?.PublicKey;

		// From https://stackoverflow.com/questions/13806299/how-can-i-create-a-self-signed-certificate-using-c
		private static X509Certificate2 MakeBaytCertificate(string certPath)
		{
			var ecdsa = ECDsa.Create();
			var req = new CertificateRequest("CN=BaytAPI", ecdsa, HashAlgorithmName.SHA256);
			var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));

			string parentDirName = Path.GetDirectoryName(certPath)!;
			if (!Directory.Exists(parentDirName)) Directory.CreateDirectory(parentDirName);
			if (File.Exists(certPath)) File.Delete(certPath);

			File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx));
			Logs.LogBook.Write(new (StreamId.Verbose, "Certificate generation", $"Bayt certificate generated. (Thumb: '{cert.Thumbprint}', Path: '{certPath}')"));

			return cert;
		}
	}
}