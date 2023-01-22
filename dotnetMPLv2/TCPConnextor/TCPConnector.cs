using SWTools;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MPL
{
    public class TCPConnector : IDisposable
    {
        protected Socket socket;
        BlockingQueue<Message> sendQueue;
        BlockingQueue<Message> recvQueue;
        Task sendTask;
        Task recvTask;

        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/volatile
        AtomicBool isSending;
        AtomicBool isReceiving;
        AtomicBool useSendQueue;
        AtomicBool useRecvQueue;

        public TCPConnector()
        {
            socket = null; // will be set when client calls "connect"
            sendQueue = new BlockingQueue<Message>();
            recvQueue = new BlockingQueue<Message>();

            isReceiving = new AtomicBool(false);
            isSending = new AtomicBool(false);

            useRecvQueue = new AtomicBool(true);
            useSendQueue = new AtomicBool(true);
        }

        public bool IsSending => isSending.get();

        public bool IsReceiving => isReceiving.get();

        public void UseRecvQueue(bool val) => useRecvQueue.set(val);

        public void UseSendQueue(bool val) => useSendQueue.set(val);

        public void UseQueues(bool val)
        {
            UseRecvQueue(val);
            UseSendQueue(val);
        }

        public bool IsConnected => socket.Connected;

        // make single attempt to connect to server at endpoint ep
        public void Connect(EndPoint ep)
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(ep);
            Start();
        }

        // make retries attempts to connect to server at endpoint ep
        public uint ConnectPersist(EndPoint ep, uint retries, uint wtime_secs, uint vlevel)
        {
            uint runAttempts = 0;
            while (runAttempts++ < retries)
            {
                try
                {
                    if (vlevel > 0)
                    {
                        Console.WriteLine($"Connection attempt # {runAttempts}  of {retries} to {ep.ToString()}");
                    }

                    Connect(ep);
                    break;
                }
                catch (Exception)
                {
                    if (vlevel > 0)
                    {
                        Console.WriteLine($"Failed Attempt: {runAttempts}");
                    }
                    Task.Delay((int)wtime_secs * 1000).Wait();
                }
            }
            return runAttempts;
        }

        // post a message into the send blocking queue
        public void PostMessage(Message msg)
        {
            sendQueue.enQ(msg);
        }

        // send the message directly over the socket, bypassing the send blocking queue
        public void SendMessage(Message msg)
        {
            SendSocketMessage(msg);
        }

        // if a valid TCP connection to server exists, that start the send/recv worker threads 
        private void Start()
        {
            if (IsConnected)
            {
                if (useSendQueue.get())
                    StartSending();

                if (useRecvQueue.get())
                    StartReceiving();
            }
        }

        // start dedicated worker thread: SendProc to handling sending message over the socket
        private void StartSending()
        {
            if (!isSending.get())
            {
                isSending.set(true);
                sendTask = Task.Run(SendProc);
            }
        }

        // cause dedicated worker thread: SendProc to shutdown: by enQing STOP_SENDING message 
        private void StopSending()
        {
            try
            {
                if (isSending.get())
                {
                    PostMessage(new Message(MessageType.STOP_SENDING));
                    sendTask.Wait();
                    isSending.set(false);
                }
            }
            catch
            {
                isSending.set(false);
            }
        }

        // dedicated worker thread to serialize messages into the socket 
        protected virtual void SendProc()
        {
            try
            {
                Message msg = sendQueue.deQ();

                // if this is the stop sending message, signal
                // the send thread to shutdown
                while (msg.msg_type != MessageType.STOP_SENDING)
                {
                    // serialize the message into the socket
                    SendSocketMessage(msg);

                    // deque the next message
                    msg = sendQueue.deQ();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("TCPConnector::SendProc: " + ex.Message); // IsSending(false);
            }
        }

        // send messsage into the socket 
        protected virtual void SendSocketMessage(Message msg)
        {
            // serialize, and send the message
            SocketUtils.SendAll(socket, msg.GetSerializedMessage, SocketFlags.None);
        }

        // deQ message from the recv blocking queue
        public Message GetMessage()
        {
            return recvQueue.deQ();
        }

        // receive message directly from the socket
        public Message RecvMessage()
        {
            return RecvSocketMessage();
        }

        // start a dedicated recv message thread, read messages from the socket
        void StartReceiving()
        {
            if (!isReceiving.get())
            {
                isReceiving.set(true);
                recvTask = Task.Run(RecvProc);
            }
        }

        // wait the receive worker thread to exit
        private void StopReceiving()
        {
            try
            {
                if (isReceiving.get())
                {
                    recvTask.Wait();
                    isReceiving.set(false);
                }
            }
            catch
            {
                isReceiving.set(false);
            }
        }

        // dedicated worker thread to deserialize messages from the socket
        protected virtual void RecvProc()
        {
            try
            {
                Message msg;
                do
                {
                    msg = RecvSocketMessage();
                    recvQueue.enQ(msg);
                }
                while (msg.msg_type != MessageType.DISCONNECT);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TCPConnector::RecvProc: " + ex.Message);
            }
        }

        // read, and deserizlize the message header and message content from the socket
        protected virtual Message RecvSocketMessage()
        {
            byte[] hdr_bytes = new byte[PackedMessage.HeaderLen];
            int recv_bytes;

            // receive fixed size message header (see wire protocol in Message.h)
            if ((recv_bytes = SocketUtils.RecvAll(socket, hdr_bytes, SocketFlags.None)) == PackedMessage.HeaderLen)
            {
                // extract message hdr attributes, message type, and content length to read out the socket
                var header_vals = PackedMessage.ExtractMessageAttribs(hdr_bytes);
                uint content_len = header_vals.Item2;
                MessageType msg_type = header_vals.Item1;

                // recv message data
                byte[] content = new byte[content_len];
                int readLen;
                if ((readLen = SocketUtils.RecvAll(socket, content, SocketFlags.None)) != content_len)
                    throw new SocketException();

                return new Message(content, msg_type);
            }

            // if zero length is read, this means the server client handler called close() on the socket
            // so signal client receive thread shutdown
            if (recv_bytes == 0)
            {
                return new Message(MessageType.DISCONNECT);
            }
            else
            {
                throw new SocketException();
            }
        }


        // cclose the socket, 
        public void Close(Thread listener = null)
        {
            if (IsConnected)
            {
                if (useSendQueue.get())
                    StopSending();
                socket.Shutdown(SocketShutdown.Send);

                if (listener != null)
                    listener.Join();

                if (useRecvQueue.get())
                    StopReceiving();
                socket.Shutdown(SocketShutdown.Receive);

                socket.Close();
            }
        }

        public void Dispose()
        {
            if (socket != null)
                socket.Dispose();
        }
    

#if TEST_Connector
        static void Main(string[] args)
        {
            string ip = "127.0.0.1";
            int port = 8080;
            const int NUM_TEST_CLIENTS = 100;
            if (args.Length == 2)
            {
                ip = args[0];
                port = int.Parse(args[1]);
            }
            Stopwatch sw = new Stopwatch();
            sw.Start();
            using (TCPConnector connector = new TCPConnector())
            {
                // example of settings that can be specified the TCP connector
                // 
                // connector.UseRecvQueue(false);
                // connector.UseSendQueue(false);
                // or set both at the same time: connector.UseQueues(false);
                // IPHostEntry host = Dns.GetHostByAddress(IPAddress.Parse("192.168.37.146"));
                // IPAddress ipAddress = host.AddressList[0];

                EndPoint ep = new IPEndPoint(IPAddress.Parse(ip), port);
               
                for (int j = 0; j < NUM_TEST_CLIENTS; ++j)
                {
                    string name = "test client " + (j + 1);
                    int num_messages = 100;

                    // - make a singke attempt to connect
                    // connector.Connect(ep); 
                    // make retries attempst to connect
                    uint retries = 10;
                    connector.ConnectPersist(ep, retries, 1, 1);
                    if (connector.IsConnected)
                    {
                        for (int i = 0; i < num_messages; ++i)
                        {
                            Message send_msg = new Message("[ Message #: " + (i + 1).ToString() + " ]", MessageType.DEFAULT);
                            Console.WriteLine($"{name}: Sending:-> {send_msg}");
                            
                            // post the message 
                            connector.PostMessage(send_msg);

                            // read the reply message
                            Message reply_msg = connector.GetMessage();
                            Console.WriteLine($"Echo Reply from server at: {ep} -> {send_msg}");

                        }
                        connector.Close();
                    }
                }
            }
            sw.Stop();

            Console.WriteLine("Elapsed={0}", sw.Elapsed);
        }
#endif
    }
}
