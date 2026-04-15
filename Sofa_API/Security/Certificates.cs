using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sofa_API.Security;


public static class Certificates
{
	static Certificates()
	{
		string certPath = Path.Combine(ApiConfig.BaseDataPath, "certs", "sofaCert.pfx");
		string futureCertPath = Path.Combine(ApiConfig.BaseDataPath, "certs", "futureSofaCert.pfx");
		X509Certificate2 certificate;

		if (!File.Exists(certPath))
		{
			Logs.LogBook.Write(new (StreamId.Info, "HTTPS Initalization", "No HTTPS certificate found. Sofa will generate one."));
			certificate = MakeSofaCertificate(certPath);
		}
		else
		{
			try
			{
				certificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, "");

				if (certificate.IsExpiredOrTooNew())
					throw new Exception("Certificate is expired or too new.");
				Logs.LogBook.Write(new (StreamId.Info, "HTTPS Initalization", "Loaded HTTPS certificate."));
			}
			catch (Exception e)
			{
				Logs.LogBook.Write(new (StreamId.Error, "HTTPS Initalization", $"Failed to load HTTPS certificate: {e.Message}"));
				certificate = MakeSofaCertificate(certPath);
			}
		}

		SofaCertificate = certificate;

		if (IsCloseToExpiration)
		{
			if (File.Exists(futureCertPath))
			{
				try
				{
					FutureSofaCertificate = X509CertificateLoader.LoadPkcs12FromFile(futureCertPath, "");
					return;
				}
				catch (Exception e)
				{
					Logs.LogBook.Write(new (StreamId.Error, "HTTPS Initalization", $"Failed to load future HTTPS certificate: {e.Message}. A new one will be generated."));
				}
			}
			else
			{
				Logs.LogBook.Write(new (StreamId.Verbose, "HTTPS Initalization",
					$"Generating a future HTTPS certificate as the current one is close to expiration ({(NotAfter - DateTime.Now).TotalDays} days left)."));
			}

			FutureSofaCertificate = MakeSofaCertificate(futureCertPath, NotAfter);
		}
	}

	internal static readonly X509Certificate2 SofaCertificate;
	public static string Thumbprint => SofaCertificate.GetCertHashString(HashAlgorithmName.SHA256);
	public static DateTime NotAfter => SofaCertificate.NotAfter;
	public static bool IsCloseToExpiration => (NotAfter - DateTime.Now).TotalDays <= 180;

	internal static X509Certificate2? FutureSofaCertificate
	{
		get
		{
			if (!IsCloseToExpiration) return null;
			if (field is not null) return field;

			string certPath = Path.Combine(ApiConfig.BaseDataPath, "certs", "futureSofaCert.pfx");

			if (File.Exists(certPath))
			{
				try
				{
					field = X509CertificateLoader.LoadPkcs12FromFile(certPath, "");
					return field;
				}
				catch (Exception e)
				{
					Logs.LogBook.Write(new (StreamId.Error, "Future TLS Loading", $"Failed to load future HTTPS certificate: {e.Message}. A new one will be generated."));
				}
			}
			else
			{
				Logs.LogBook.Write(new (StreamId.Verbose, "Future TLS Loading",
					$"Generating a future HTTPS certificate as the current one is close to expiration ({(NotAfter - DateTime.Now).TotalDays} days left)."));
			}

			field = MakeSofaCertificate(certPath, NotAfter);
			return field;
		}
		private set;
	}


	// From https://stackoverflow.com/questions/13806299/how-can-i-create-a-self-signed-certificate-using-c
	private static X509Certificate2 MakeSofaCertificate(string certPath, DateTimeOffset? baseDate = null)
	{
		baseDate ??= DateTimeOffset.Now;

		var ecdsa = ECDsa.Create();
		var req = new CertificateRequest("CN=SofaAPI", ecdsa, HashAlgorithmName.SHA256);
		var cert = req.CreateSelfSigned(baseDate.Value, baseDate.Value.AddYears(2));

		string parentDirName = Path.GetDirectoryName(certPath)!;
		if (!Directory.Exists(parentDirName)) Directory.CreateDirectory(parentDirName);
		if (File.Exists(certPath)) File.Delete(certPath);

		File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx));
		Logs.LogBook.Write(new (StreamId.Verbose, "Certificate generation", $"Sofa certificate generated. (Thumb: '{cert.GetCertHashString(HashAlgorithmName.SHA256)}')"));

		return cert;
	}
	public static bool IsExpiredOrTooNew(this X509Certificate2 cert)
	{
		var now = DateTime.Now;
		return now > cert.NotAfter || now < cert.NotBefore;
	}
}