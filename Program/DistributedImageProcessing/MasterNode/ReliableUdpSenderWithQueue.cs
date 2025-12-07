using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MasterNode
{
    public class ReliableUdpSenderWithQueue
    {
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new();
        private readonly BlockingCollection<(byte[] data, IPEndPoint ep)> _queue;

        private int _sequence = 0;

        public ReliableUdpSenderWithQueue()
        {
            _udp = new UdpClient();
            _udp.Client.ReceiveTimeout = 300;

            _queue = new BlockingCollection<(byte[], IPEndPoint)>(new ConcurrentQueue<(byte[], IPEndPoint)>());

            Task.Run(SendLoopAsync);
        }

        public void Enqueue(byte[] data, IPEndPoint endpoint)
        {
            _queue.Add((data, endpoint));
        }

        private async Task SendLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var (data, endpoint) = _queue.Take(_cts.Token);

                    await SendReliableAsync(data, endpoint);

                    //await Task.Delay(1);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task SendReliableAsync(byte[] data, IPEndPoint endpoint)
        {
            int seq = Interlocked.Increment(ref _sequence);

            byte[] packet = new byte[data.Length + 4];
            BitConverter.GetBytes(seq).CopyTo(packet, 0);
            Buffer.BlockCopy(data, 0, packet, 4, data.Length);

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                await _udp.SendAsync(packet, packet.Length, endpoint);

                try
                {
                    var resTask = _udp.ReceiveAsync();
                    var res = await resTask;

                    int ackSeq = BitConverter.ToInt32(res.Buffer, 0);

                    if (ackSeq == seq)
                        return; // доставлено
                }
                catch
                {
                    // ignore timeout, retry
                }

                //await Task.Delay(1);
            }

            Console.WriteLine($"[UDP] НЕ доставлено после 5 попыток → seq {seq}");
        }

        public void Close()
        {
            _cts.Cancel();
            _udp.Close();
        }
    }


}
