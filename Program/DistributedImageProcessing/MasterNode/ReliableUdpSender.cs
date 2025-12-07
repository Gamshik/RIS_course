using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MasterNode
{
    public class ReliableUdpSender
    {
        private readonly UdpClient _udp;
        private int _sequence = 0;

        public ReliableUdpSender()
        {
            _udp = new UdpClient();
            _udp.Client.ReceiveTimeout = 300;
            _udp.Client.SendBufferSize = 1024 * 1024;
        }

        public async Task<bool> SendWithAckAsync(byte[] data, IPEndPoint endpoint)
        {
            int seq = Interlocked.Increment(ref _sequence);

            // Добавляем 4 байта SequenceId в начало пакета
            byte[] packet = new byte[data.Length + 4];
            BitConverter.GetBytes(seq).CopyTo(packet, 0);
            Buffer.BlockCopy(data, 0, packet, 4, data.Length);

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                await _udp.SendAsync(packet, packet.Length, endpoint);

                try
                {
                    var receiveTask = _udp.ReceiveAsync();
                    var result = await receiveTask;

                    // Ожидаем ACK <seq>
                    int ack = BitConverter.ToInt32(result.Buffer, 0);

                    if (ack == seq)
                        return true; // подтверждение получено
                }
                catch
                {
                    // timeout → повторяем
                }

                await Task.Delay(20);
            }

            return false; // не получен ACK
        }

        public void Close()
        {
            try { _udp?.Close(); } catch { }
        }
    }

}
