using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FileCat.Core.Inspect;

/// <summary>
/// A CMS (PKCS #7) SignedData blob read without verifying anything: the certificates it carries and which of them the
/// signer names. For code signatures whose trust FileCat does not judge (Mach-O's, for one).
/// </summary>
internal static class SignedData
{
    public static (string? Signer, List<string[]> Certificates)? Read(ReadOnlyMemory<byte> blob)
    {
        var loaded = new List<X509Certificate2>();
        try
        {
            var contentInfo = new AsnReader(blob, AsnEncodingRules.BER).ReadSequence();
            if (contentInfo.ReadObjectIdentifier() != "1.2.840.113549.1.7.2") return null;
            var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
            signedData.ReadInteger();
            signedData.ReadSetOf();
            signedData.ReadSequence();
            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                var set = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
                while (set.HasData && loaded.Count < 32)
                {
                    var der = set.ReadEncodedValue();
                    if (Asn1Tag.Decode(der.Span, out _).HasSameClassAndValue(Asn1Tag.Sequence)) loaded.Add(X509CertificateLoader.LoadCertificate(der.Span));
                }
            }
            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1))) signedData.ReadEncodedValue();
            X509Certificate2? signer = null;
            var signerInfos = signedData.ReadSetOf();
            if (signerInfos.HasData)
            {
                var info = signerInfos.ReadSequence();
                info.ReadInteger();
                if (info.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                {
                    // Issuer and serial number.
                    var sid = info.ReadSequence();
                    byte[] issuer = sid.ReadEncodedValue().ToArray();
                    var serial = sid.ReadIntegerBytes().ToArray();
                    signer = loaded.FirstOrDefault(c => c.IssuerName.RawData.AsSpan().SequenceEqual(issuer) && Trim(c.SerialNumberBytes.Span).SequenceEqual(Trim(serial)));
                }
                else
                {
                    // Subject key identifier.
                    var keyId = info.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
                    signer = loaded.FirstOrDefault(c => c.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault()?.SubjectKeyIdentifierBytes.Span.SequenceEqual(keyId) == true);
                }
            }
            var rows = loaded.Select(c => new[] { c.GetNameInfo(X509NameType.SimpleName, false), c.GetNameInfo(X509NameType.SimpleName, true),
                $"{c.NotBefore.ToUniversalTime():yyyy-MM-dd}", $"{c.NotAfter.ToUniversalTime():yyyy-MM-dd}", c.Thumbprint }).ToList();
            return (signer?.GetNameInfo(X509NameType.SimpleName, false), rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A damaged or hostile blob: nothing to show from it.
            return null;
        }
        finally
        {
            foreach (var certificate in loaded) certificate.Dispose();
        }
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> serial)
    {
        int i = 0;
        while (i < serial.Length - 1 && serial[i] == 0) i++;
        return serial[i..];
    }
}
