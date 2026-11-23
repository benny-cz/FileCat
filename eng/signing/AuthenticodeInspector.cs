// Reads a PE file's Authenticode signature without trusting the signing tool that produced it: Windows' own trust
// decision (WinVerifyTrust), the embedded PKCS#7 (signer, digest algorithms, nested signatures) and the RFC 3161
// timestamp, which is checked against the signature it claims to cover. Loaded by the eng/signing scripts.
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace FileCat.Signing
{
    public sealed class SignatureFacts
    {
        public string Path;
        public bool IsPe;
        /// <summary>WinVerifyTrust's HRESULT; 0 means Windows trusts the signature.</summary>
        public int TrustResult;
        /// <summary>Number of PKCS#7 entries in the PE certificate table.</summary>
        public int SignatureBlobs;
        public int SignerInfos;
        public int NestedSignatures;
        public string ContentType;
        public string FileDigestAlgorithm;
        /// <summary>The image digest the signature carries, and the one computed here from the file's bytes.</summary>
        public string SignedFileDigest;
        public string ComputedFileDigest;
        /// <summary>The SignerInfo's signature verifies over its signed attributes, whose message digest matches the content.</summary>
        public bool SignatureVerifies;
        public string SignerDigestAlgorithm;
        public X509Certificate2 Signer;
        public X509Certificate2Collection EmbeddedCertificates;
        public int Rfc3161Timestamps;
        public bool TimestampCoversSignature;
        public string TimestampHashAlgorithm;
        public DateTimeOffset? TimestampUtc;
        public X509Certificate2 TimestampSigner;
        public X509Certificate2Collection TimestampCertificates;
        public bool LegacyCountersignature;
        /// <summary>Offset and size of the certificate table, and whether it ends the file.</summary>
        public long CertificateTableOffset;
        public long CertificateTableSize;
        public bool CertificateTableAtEnd;
        public string Error;
    }

    public static class Authenticode
    {
        public const string SpcIndirectData = "1.3.6.1.4.1.311.2.1.4";
        public const string Rfc3161Attribute = "1.3.6.1.4.1.311.3.3.1";
        public const string NestedSignatureAttribute = "1.3.6.1.4.1.311.2.4.1";
        public const string LegacyCountersignatureAttribute = "1.2.840.113549.1.9.6";

        const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_REVOKE_WHOLECHAIN = 1, WTD_CHOICE_FILE = 1,
            WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2, WTD_REVOCATION_CHECK_NONE = 0x10,
            WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x80;
        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        static extern int WinVerifyTrust(IntPtr hwnd, [In] ref Guid action, [In, Out] ref WINTRUST_DATA data);

        /// <summary>Windows' Authenticode verdict for the file, with no UI; revocation is checked only when asked.</summary>
        public static int Verify(string path, bool checkRevocation)
        {
            var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = path };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = checkRevocation ? WTD_REVOKE_WHOLECHAIN : WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = checkRevocation ? WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT : WTD_REVOCATION_CHECK_NONE,
                };
                Guid action = GenericVerifyV2;
                int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                return result;
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
        }

        /// <summary>Locates the PE security directory entry. Returns false for a file that is not a PE image.</summary>
        public static bool TryLocateCertificateTable(byte[] bytes, out int directoryEntry, out long offset, out long size)
        {
            directoryEntry = -1; offset = 0; size = 0;
            if (bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') return false;
            int pe = BitConverter.ToInt32(bytes, 0x3C);
            if (pe <= 0 || pe > bytes.Length - 26 || BitConverter.ToUInt32(bytes, pe) != 0x00004550) return false;
            int optional = pe + 24;
            ushort magic = BitConverter.ToUInt16(bytes, optional);
            int directories = magic == 0x20B ? optional + 112 : magic == 0x10B ? optional + 96 : -1;
            if (directories < 0 || directories + 5 * 8 > bytes.Length) throw new InvalidDataException("Unsupported PE optional header.");
            if (BitConverter.ToUInt32(bytes, directories - 4) <= 4) return true;
            directoryEntry = directories + 4 * 8;
            offset = BitConverter.ToUInt32(bytes, directoryEntry);
            size = BitConverter.ToUInt32(bytes, directoryEntry + 4);
            if (offset + size > bytes.LongLength) throw new InvalidDataException("The certificate table extends past the end of the file.");
            return true;
        }

        /// <summary>The PKCS#7 SignedData blobs (WIN_CERT_TYPE_PKCS_SIGNED_DATA) of the certificate table, trimmed to their DER length.</summary>
        public static List<byte[]> ReadSignatureBlobs(byte[] bytes, long offset, long size)
        {
            var blobs = new List<byte[]>();
            long position = offset, end = offset + size;
            while (size > 0 && position + 8 <= end)
            {
                uint length = BitConverter.ToUInt32(bytes, (int)position);
                ushort type = BitConverter.ToUInt16(bytes, (int)position + 6);
                if (length < 8 || position + length > end) throw new InvalidDataException("Malformed WIN_CERTIFICATE entry.");
                if (type == 2)
                {
                    var content = new ReadOnlySpan<byte>(bytes, (int)position + 8, (int)length - 8);
                    int contentOffset, contentLength, consumed;
                    AsnDecoder.ReadEncodedValue(content, AsnEncodingRules.BER, out contentOffset, out contentLength, out consumed);
                    blobs.Add(content.Slice(0, consumed).ToArray());
                }
                position += (length + 7) & ~7u;
            }
            return blobs;
        }

        public static SignatureFacts Inspect(string path, bool checkRevocation)
        {
            var facts = new SignatureFacts { Path = path };
            byte[] bytes = File.ReadAllBytes(path);
            int directoryEntry; long offset, size;
            try
            {
                facts.IsPe = TryLocateCertificateTable(bytes, out directoryEntry, out offset, out size);
            }
            catch (InvalidDataException ex)
            {
                facts.IsPe = true;
                facts.Error = ex.Message;
                return facts;
            }
            if (!facts.IsPe) return facts;
            facts.CertificateTableOffset = offset;
            facts.CertificateTableSize = size;
            facts.CertificateTableAtEnd = size > 0 && offset + size == bytes.LongLength;
            facts.TrustResult = Verify(path, checkRevocation);
            try
            {
                List<byte[]> blobs = ReadSignatureBlobs(bytes, offset, size);
                facts.SignatureBlobs = blobs.Count;
                if (blobs.Count == 0) return facts;
                var cms = new SignedCms();
                cms.Decode(blobs[0]);
                facts.ContentType = cms.ContentInfo.ContentType.Value;
                facts.EmbeddedCertificates = cms.Certificates;
                facts.SignerInfos = cms.SignerInfos.Count;
                if (facts.ContentType == SpcIndirectData)
                {
                    byte[] signedDigest;
                    facts.FileDigestAlgorithm = IndirectDataDigest(cms.ContentInfo.Content, out signedDigest);
                    facts.SignedFileDigest = Convert.ToHexString(signedDigest);
                    byte[] computed = ImageDigest(bytes, facts.FileDigestAlgorithm, offset, size);
                    if (computed != null) facts.ComputedFileDigest = Convert.ToHexString(computed);
                }
                if (cms.SignerInfos.Count == 0) return facts;
                try
                {
                    cms.CheckSignature(true);
                    facts.SignatureVerifies = true;
                }
                catch (CryptographicException ex)
                {
                    facts.Error = "signature: " + ex.Message;
                }
                SignerInfo signer = cms.SignerInfos[0];
                facts.SignerDigestAlgorithm = signer.DigestAlgorithm.Value;
                facts.Signer = signer.Certificate;
                foreach (CryptographicAttributeObject attribute in signer.UnsignedAttributes)
                {
                    if (attribute.Oid.Value == Rfc3161Attribute)
                    {
                        foreach (AsnEncodedData value in attribute.Values)
                        {
                            facts.Rfc3161Timestamps++;
                            Rfc3161TimestampToken token;
                            int consumed;
                            if (!Rfc3161TimestampToken.TryDecode(value.RawData, out token, out consumed)) continue;
                            facts.TimestampUtc = token.TokenInfo.Timestamp;
                            facts.TimestampHashAlgorithm = token.TokenInfo.HashAlgorithmId.Value;
                            facts.TimestampCertificates = token.AsSignedCms().Certificates;
                            X509Certificate2 tsa;
                            facts.TimestampCoversSignature = token.VerifySignatureForSignerInfo(signer, out tsa, facts.TimestampCertificates);
                            facts.TimestampSigner = tsa;
                        }
                    }
                    else if (attribute.Oid.Value == NestedSignatureAttribute) facts.NestedSignatures += attribute.Values.Count;
                    else if (attribute.Oid.Value == LegacyCountersignatureAttribute) facts.LegacyCountersignature = true;
                }
            }
            catch (Exception ex) when (ex is CryptographicException || ex is AsnContentException || ex is InvalidDataException)
            {
                facts.Error = ex.GetType().Name + ": " + ex.Message;
            }
            return facts;
        }

        // SpcIndirectDataContent ::= SEQUENCE { data SpcAttributeTypeAndOptionalValue, messageDigest DigestInfo }
        // DigestInfo ::= SEQUENCE { digestAlgorithm AlgorithmIdentifier, digest OCTET STRING }
        static string IndirectDataDigest(byte[] content, out byte[] digest)
        {
            var reader = new AsnReader(content, AsnEncodingRules.BER);
            AsnReader indirect = reader.ReadSequence();
            indirect.ReadEncodedValue();
            AsnReader digestInfo = indirect.ReadSequence();
            string algorithm = digestInfo.ReadSequence().ReadObjectIdentifier();
            digest = digestInfo.ReadOctetString();
            return algorithm;
        }

        /// <summary>The Authenticode image digest computed independently of Windows: every byte of the file except the
        /// optional header's CheckSum, the security directory entry and the certificate table. Null for an unknown algorithm.</summary>
        public static byte[] ImageDigest(byte[] bytes, string algorithm, long certificateTableOffset, long certificateTableSize)
        {
            HashAlgorithmName name;
            switch (algorithm)
            {
                case "2.16.840.1.101.3.4.2.1": name = HashAlgorithmName.SHA256; break;
                case "2.16.840.1.101.3.4.2.2": name = HashAlgorithmName.SHA384; break;
                case "2.16.840.1.101.3.4.2.3": name = HashAlgorithmName.SHA512; break;
                case "1.3.14.3.2.26": name = HashAlgorithmName.SHA1; break;
                default: return null;
            }
            int directoryEntry; long offset, size;
            TryLocateCertificateTable(bytes, out directoryEntry, out offset, out size);
            int checksum = BitConverter.ToInt32(bytes, 0x3C) + 24 + 64;
            var excluded = new List<KeyValuePair<long, long>>
            {
                new KeyValuePair<long, long>(checksum, 4),
                new KeyValuePair<long, long>(directoryEntry, 8),
                new KeyValuePair<long, long>(certificateTableOffset, certificateTableSize),
            };
            excluded.Sort((a, b) => a.Key.CompareTo(b.Key));
            using (IncrementalHash hash = IncrementalHash.CreateHash(name))
            {
                long position = 0;
                foreach (KeyValuePair<long, long> range in excluded)
                {
                    if (range.Value == 0 || range.Key < position) continue;
                    hash.AppendData(bytes, (int)position, (int)(range.Key - position));
                    position = range.Key + range.Value;
                }
                if (position < bytes.LongLength) hash.AppendData(bytes, (int)position, (int)(bytes.LongLength - position));
                return hash.GetHashAndReset();
            }
        }

        /// <summary>Builds the signer's chain at the given time against Windows' roots, or only against
        /// <paramref name="customRoot"/> when one is given. Returns null when it builds, otherwise the chain errors.</summary>
        public static string ValidateChain(X509Certificate2 leaf, X509Certificate2Collection extra, X509Certificate2 customRoot,
            DateTime verificationTime, string applicationPolicy)
        {
            using (var chain = new X509Chain())
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationTime = verificationTime;
                if (extra != null) chain.ChainPolicy.ExtraStore.AddRange(extra);
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(applicationPolicy));
                if (customRoot != null)
                {
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(customRoot);
                }
                if (chain.Build(leaf)) return null;
                var errors = new List<string>();
                foreach (X509ChainStatus status in chain.ChainStatus) errors.Add(status.Status + ": " + status.StatusInformation.Trim());
                return errors.Count == 0 ? "chain did not build" : string.Join("; ", errors);
            }
        }

        /// <summary>A copy of a signed PE whose certificate table holds <paramref name="pkcs7"/> instead
        /// (the table must end the file). Used to build negative controls from real signed outputs.</summary>
        public static byte[] ReplaceSignature(byte[] bytes, byte[] pkcs7)
        {
            int directoryEntry; long offset, size;
            if (!TryLocateCertificateTable(bytes, out directoryEntry, out offset, out size) || size == 0 || offset + size != bytes.LongLength)
                throw new InvalidDataException("Expected a signed PE whose certificate table ends the file.");
            using (var output = new MemoryStream())
            {
                output.Write(bytes, 0, (int)offset);
                if (pkcs7 == null)
                {
                    byte[] stripped = output.ToArray();
                    Array.Clear(stripped, directoryEntry, 8);
                    return stripped;
                }
                int entryLength = (8 + pkcs7.Length + 7) & ~7;
                output.Write(BitConverter.GetBytes(entryLength), 0, 4);
                output.Write(BitConverter.GetBytes((ushort)0x0200), 0, 2);
                output.Write(BitConverter.GetBytes((ushort)0x0002), 0, 2);
                output.Write(pkcs7, 0, pkcs7.Length);
                output.Write(new byte[entryLength - 8 - pkcs7.Length], 0, entryLength - 8 - pkcs7.Length);
                byte[] result = output.ToArray();
                Array.Copy(BitConverter.GetBytes((uint)entryLength), 0, result, directoryEntry + 4, 4);
                return result;
            }
        }

        /// <summary>The signature with its RFC 3161 timestamp removed (unsigned attributes are outside the signature).</summary>
        public static byte[] WithoutTimestamp(byte[] pkcs7)
        {
            var cms = new SignedCms();
            cms.Decode(pkcs7);
            var timestamps = new List<AsnEncodedData>();
            foreach (CryptographicAttributeObject attribute in cms.SignerInfos[0].UnsignedAttributes)
                if (attribute.Oid.Value == Rfc3161Attribute)
                    foreach (AsnEncodedData value in attribute.Values) timestamps.Add(new AsnEncodedData(attribute.Oid, value.RawData));
            if (timestamps.Count == 0) throw new InvalidDataException("The signature has no RFC 3161 timestamp to remove.");
            // Each removal changes the document, so the signer is fetched again every time.
            foreach (AsnEncodedData timestamp in timestamps) cms.SignerInfos[0].RemoveUnsignedAttribute(timestamp);
            return cms.Encode();
        }
    }
}
