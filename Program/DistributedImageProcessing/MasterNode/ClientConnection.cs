using System.Net;
using System.Net.Sockets;

public class ClientConnection
{
    public NetworkStream Stream { get; set; }
    public IPEndPoint ClientUdpEndpoint { get; set; }
}