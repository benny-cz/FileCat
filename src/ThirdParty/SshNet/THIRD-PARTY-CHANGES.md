# SSH.NET source build

FileCat builds the `src/Renci.SshNet` C# sources from SSH.NET 2026.0.0, commit
`7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e`. The upstream source archive SHA-256 is
`e3c305e2bf41d00f7aba51b9cdd151567dfe32c616ad322561ac59e3d6a4593b`.
The shipped license texts are in `licenses/sshnet`. [Pinned source](https://github.com/sshnet/SSH.NET/tree/7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e)

- `FileCat.SshNet.csproj` builds the .NET 10 sources in FileCat's locked graph as `FileCat.SshNet.dll`. It retains BouncyCastle.Cryptography 2.7.0 and Microsoft.Extensions.Logging.Abstractions 8.0.3. The client identification in `Session.cs` takes the version from the built assembly.
- `SftpFileAttributes.cs` retains the v3 field flags, unsigned IDs and native permission mask from the original reply in `SftpWireAttributes`.
- The `FileCatSecurity` partial classes add exact-path LSTAT, versioned extension discovery, a typed single-component lsetstat request and a bounded users-groups-by-id reply. They use the existing SFTP session, request dispatch and host-key verification.
