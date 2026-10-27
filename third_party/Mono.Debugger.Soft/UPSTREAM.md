# Mono.Debugger.Soft source provenance

Source: https://github.com/mono/debugger-libs
Commit: e7fbb713d156d11193ed404783ad6fe9c4042a6d

LICENSE is the upstream MIT license. Locale.cs is unmodified. Vendored changes consist of replacing the `Microsoft.FileFormats.PE` imports in AssemblyMirror.cs and Connection.cs with the local PdbChecksum DTO, and the GetLocals compatibility correction below. This removes the dependency on obsolete symbol packages from the upstream development feed.

The local project targets .NET 10 and references Mono.Cecil 0.11.6. PdbChecksum.cs supplies the small checksum DTO used by the metadata API, replacing that type from Microsoft.FileFormats. LocalTcpConnector.cs permits cancellation and handshake timeouts on an externally owned loopback socket.

MethodMirror.GetLocals has a compatibility correction: when a native agent advertises protocol 2.65+ but omits the optional local-slot index array, use its legacy sequential slots and names unless an external PDB map is available. Upstream assumed that a browser client had supplied a PDB map and threw NullReferenceException on native .NET 8 Mono. The live integration suite exercises this fallback. The vendor project pins C# 12 to preserve the meaning of upstream fields named `field` under newer compilers.
