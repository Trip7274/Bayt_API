using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sofa_API.Security;


public static class Certificates
{
	static Certificates()
	{
		string certPath = Path.Combine(ApiConfig.BaseDataPath, "certs", "sofaCert.pfx");
		if (!File.Exists(certPath))
		{
			Logs.LogBook.Write(new (StreamId.Info, "HTTPS Initalization", "No HTTPS certificate found. Sofa will generate one."));
			SofaCertificate = MakeSofaCertificate(certPath);
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
				Logs.LogBook.Write(new (StreamId.Error, "HTTPS Initalization", "Failed to load HTTPS certificate due to some internal error. Sofa will continue without HTTPS."));
				break;
			}

			var now = DateTime.Now;
			if (now > certificate.NotAfter || now < certificate.NotBefore)
			{
				Logs.LogBook.Write(new (StreamId.Verbose, "HTTPS Initalization", "HTTPS certificate is expired. Sofa will generate a new one."));
				MakeSofaCertificate(certPath);
			}
			else
			{
				Logs.LogBook.Write(new (StreamId.Info, "HTTPS Initalization", "Loaded HTTPS certificate."));
				break;
			}
		}

		if (certificate is not null) SofaCertificate = certificate;
	}

	internal static readonly X509Certificate2? SofaCertificate;
	public static PublicKey? SofaPublicKey => SofaCertificate?.PublicKey;

	// From https://stackoverflow.com/questions/13806299/how-can-i-create-a-self-signed-certificate-using-c
	private static X509Certificate2 MakeSofaCertificate(string certPath)
	{
		var ecdsa = ECDsa.Create();
		var req = new CertificateRequest("CN=SofaAPI", ecdsa, HashAlgorithmName.SHA256);
		var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));

		string parentDirName = Path.GetDirectoryName(certPath)!;
		if (!Directory.Exists(parentDirName)) Directory.CreateDirectory(parentDirName);
		if (File.Exists(certPath)) File.Delete(certPath);

		File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx));
		Logs.LogBook.Write(new (StreamId.Verbose, "Certificate generation", $"Sofa certificate generated. (Thumb: '{cert.Thumbprint}', Path: '{certPath}')"));

		return cert;
	}
}