// FileCat addition (MIT): a job-local key/transform broker, avoiding retained immutable password strings.
using System;
using System.Security.Cryptography;

namespace SharpCompress.Compressors.LZMA.Utilities;

internal delegate ICryptoTransform SevenZipAesDecryptorFactory(int countPower, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> iv);
