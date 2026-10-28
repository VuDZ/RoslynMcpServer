using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PipeRelay.Checks;

internal static class WindowsAccessCheck
{
    public static object Run(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new { status = "not-run", reason = "Use the external different-UID check on Unix." };
        }

        var user = WindowsIdentity.GetCurrent().User!;
        using (var own = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
        {
            own.Connect(1000);
            var error = GetSecurityInfo(own.SafePipeHandle.DangerousGetHandle(), 6, 5,
                out _, out _, out _, out _, out var descriptor);
            if (error != 0)
            {
                throw new Win32Exception((int)error);
            }

            try
            {
                var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
                Marshal.Copy(descriptor, bytes, 0, bytes.Length);
                var security = new RawSecurityDescriptor(bytes, 0);
                SpikeChecks.Require(security.Owner == user && security.DiscretionaryAcl is not null,
                    "The pipe must have the owning user and an explicit DACL.");
                foreach (var ace in security.DiscretionaryAcl!.OfType<CommonAce>())
                {
                    SpikeChecks.Require(ace.AceQualifier != AceQualifier.AccessAllowed || ace.SecurityIdentifier == user,
                        "The pipe DACL must allow only the owning user.");
                }
            }
            finally
            {
                LocalFree(descriptor);
            }
        }

        if (!ImpersonateAnonymousToken(GetCurrentThread()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var denied = false;
        try
        {
            SpikeChecks.Require(WindowsIdentity.GetCurrent().User?.Value == "S-1-5-7", "Anonymous OS identity was not established.");
            try
            {
                using var other = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                other.Connect(1000);
            }
            catch (UnauthorizedAccessException)
            {
                denied = true;
            }
        }
        finally
        {
            if (!RevertToSelf())
            {
                Environment.FailFast("Failed to restore the test runner identity.");
            }
        }

        SpikeChecks.Require(WindowsIdentity.GetCurrent().User == user && denied,
            "A different OS identity must be denied and the runner identity must be restored.");
        return new { status = "pass", ownerOnlyDacl = true, differentPrincipal = "anonymous", connectionDenied = true };
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImpersonateAnonymousToken(IntPtr thread);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RevertToSelf();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(IntPtr handle, int objectType, uint information,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
