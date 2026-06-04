namespace RoslynMcpServer.Services;

/// <summary>Maps BCL/facade assembly names to NuGet package folder ids (Version 0.0.0.0 refs).</summary>
internal static class BclAssemblyPackageMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System.IO.Ports"] = "system.io.ports",
        ["System.Drawing.Common"] = "system.drawing.common",
        ["System.Security.Cryptography.Xml"] = "system.security.cryptography.xml",
        ["System.Security.Cryptography.Pkcs"] = "system.security.cryptography.pkcs",
        ["System.Security.Cryptography.ProtectedData"] = "system.security.cryptography.protecteddata",
        ["System.Diagnostics.EventLog"] = "system.diagnostics.eventlog",
        ["System.DirectoryServices"] = "system.directoryservices",
        ["System.DirectoryServices.Protocols"] = "system.directoryservices.protocols",
        ["System.ServiceProcess.ServiceController"] = "system.serviceprocess.servicecontroller",
        ["System.Windows.Extensions"] = "system.windows.extensions",
        ["System.Configuration.ConfigurationManager"] = "system.configuration.configurationmanager",
        ["Microsoft.Win32.SystemEvents"] = "microsoft.win32.systemevents",
        ["Microsoft.Win32.Registry"] = "microsoft.win32.registry",
    };

    public static bool TryGetPackageId(string assemblyName, out string packageId) =>
        Map.TryGetValue(assemblyName, out packageId!);
}
