using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Breakpoint.Core;
using Microsoft.Win32.SafeHandles;

namespace Breakpoint.Service;

public sealed class PipeServer(CurfewEngine engine, string pipeName, string ownerSid)
{
    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var pipe = CreatePipe();
            await pipe.WaitForConnectionAsync(stop);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = await Protocol.ReadAsync<Request>(pipe, timeout.Token);
                var administrator = false;
                string? callerSid = null;
                pipe.RunAsClient(() =>
                {
                    using var identity = WindowsIdentity.GetCurrent(true);
                    if (identity is null) return;
                    callerSid = identity.User?.Value;
                    administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                });
                if (callerSid != ownerSid && !administrator) throw new UnauthorizedAccessException("Wrong Windows account.");
                if (request.Id.Length is < 1 or > 64 || request.Action.Length > 32 || (request.Pin?.Length ?? 0) > 12 ||
                    (request.RecoveryCode?.Length ?? 0) > 100) throw new InvalidDataException("Invalid command.");
                var response = await engine.HandleAsync(request, administrator);
                await Protocol.WriteAsync(pipe, response, timeout.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
            { /* Untrusted requests cannot terminate the service; no request secrets are logged. */ }
        }
    }
    private NamedPipeServerStream CreatePipe()
    {
        // Only the installed Windows account, SYSTEM and administrators; reject remote pipe clients at the OS boundary.
        var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;{ownerSid})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe($"\\\\.\\pipe\\{pipeName}", 3 | 0x40000000 | 0x00080000, 0x8, 1, Protocol.MaxBytes, Protocol.MaxBytes, 0, ref attributes);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { LocalFree(descriptor); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", EntryPoint="ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", EntryPoint="CreateNamedPipeW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
