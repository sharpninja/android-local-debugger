using System.Net.Sockets;

namespace Mono.Debugger.Soft
{
    // Small adapter in this assembly so the caller owns socket cancellation and
    // handshake timeouts. Upstream TcpConnection is intentionally internal.
    public static class LocalTcpConnector
    {
        public static VirtualMachine Connect(Socket socket)
        {
            return VirtualMachineManager.Connect(new TcpConnection(socket), null, null);
        }
    }
}
