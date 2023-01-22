using SWTools;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks; 

namespace MPL
{
    public abstract class ClientHandler
    {
        private Socket client_socket;
        // could use Tasks instead of threads
        // private Task sendTask;
        // private Task recvTask;
        private Thread sendThread;
        private Thread recvThread;
        private BlockingQueue<Message> sendQ_;
        private BlockingQueue<Message> recvQ_;
        private AtomicBool isSending;
        private AtomicBool isReceiving;

        private AtomicBool useSendQueue;
        private AtomicBool useRecvQueue;

        EndPoint service_ep_;
        public bool UseAppProc { get; set; }

        public ClientHandler()
        {
            sendQ_ = new BlockingQueue<Message>();
            recvQ_ = new BlockingQueue<Message>();

            useRecvQueue = new AtomicBool(true);
            useSendQueue = new AtomicBool(true);

            isReceiving = new AtomicBool();
            isSending = new AtomicBool();

            service_ep_ = null;

            client_socket = null;
        }

        public abstract ClientHandler Clone();

        public abstract void AppProc();

        public void UseRecvQueue(bool val) => useRecvQueue.set(val);

        public void UseSendQueue(bool val) => useSendQueue.set(val);

        public void UseQueues(bool val)
        {
            UseRecvQueue(val);
            UseSendQueue(val);
        }

        public EndPoint RemoteEP => client_socket.RemoteEndPoint;

        public void SetServiceEndPoint(EndPoint ep) => service_ep_ = ep;

        public EndPoint GetServiceEndPoint => service_ep_;

        public void SetSocket(Socket sock)
        {
            client_socket = sock;
        }

        public void Close()
        {
            client_socket.Close();
        }

        public Socket GetDataSocket()
        {
            return client_socket;
        }

        public void PostMessage(Message msg)
        {
            sendQ_.enQ(msg);
        }

        public void SendMessage(Message m)
        {
            SendSocketMessage(m);
        }

        protected virtual void SendSocketMessage(Message msg)
        {
             // send the serialized message
            SocketUtils.SendAll(client_socket, msg.GetSerializedMessage, SocketFlags.None);

            //  // send the data
           //  SocketUtils.SendAll(client_socket, msg.GetSerializedMessage, SocketFlags.None);
        }

        protected virtual void SendProc()
        {
            try
            {
                Message msg = sendQ_.deQ();
                while (msg.msg_type != MessageType.STOP_SENDING)
                {
                    // serialize the message into the socket
                    SendSocketMessage(msg);
                    msg = sendQ_.deQ();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("ClientHandler::Sendproc() " + ex.Message);
            }
        }

           
        public void StartSending()
        {
            if (!isSending.get())
            {
                isSending.set(true);
                //sendTask = Task.Run(SendProc);
                sendThread = new Thread(SendProc);
                sendThread.Start();
            }
        }

        public void StopSending()
        {
            try
            {
                if (isSending.get())
                {
                    PostMessage(new Message(MessageType.STOP_SENDING));
                    sendThread.Join();
                    //sendTask.Wait();
                    isSending.set(false);
                }
            }
            catch
            {
                isSending.set(false);
            } 
        }

        public void ShutdownSend()
        {
            client_socket.Shutdown(SocketShutdown.Send);
        }
        
        public void ShutdownRecv()
        {
            client_socket.Shutdown(SocketShutdown.Receive);
        }

        protected virtual void RecvProc()
        {
            Message msg = null;
            try
            {        
                do
                {
                    msg = RecvSocketMessage();
                    recvQ_.enQ(msg);
                }
                while (msg.msg_type != MessageType.DISCONNECT);
            }
            catch(Exception ex)
            {
                Console.WriteLine("ClientHandler::RecvProc(): {0} Message len: {1}", ex.Message);
            }
        }

        public void StartReceiving()
        {
            if (!isReceiving.get())
            {
                isReceiving.set(true);
                //recvTask = Task.Run(RecvProc);
                recvThread = new Thread(RecvProc);
                recvThread.Start();
            }
        }

        public void StopReceiving()
        {
            try
            {
                if (isReceiving.get())
                {
                    //recvTask.Wait();
                    recvThread.Join();
                    isReceiving.set(false);
                }
            }
            catch
            {
                isReceiving.set(false);
            }
        }


        public Message GetMessage()
        {
            return recvQ_.deQ();
        }

        public Message ReceiveMessage()
        {
            return RecvSocketMessage();
        }

       
        // serialize the message header and message and write them into the socket
        protected virtual Message RecvSocketMessage()
        {    
            byte[] hdr_bytes = new byte[PackedMessage.HeaderLen];
            
            int recv_bytes;

            // receive fixed size message header (see wire protocol in Message.h)
            if ((recv_bytes = SocketUtils.RecvAll(client_socket, hdr_bytes, SocketFlags.None)) == PackedMessage.HeaderLen)
            {
                // extract message hdr attributes, message type, and content length to read out the socket
                var header_vals = PackedMessage.ExtractMessageAttribs(hdr_bytes);
                uint content_len = header_vals.Item2;
                MessageType msg_type = header_vals.Item1;

                // recv message data
                byte[] content = new byte[content_len];
                int readLen;
                if ((readLen = SocketUtils.RecvAll(client_socket, content, SocketFlags.None)) != content_len)
                    throw new SocketException();

                return new Message(content, msg_type);
            }

            // if read zero bytes, then this is the zero length message signaling client shutdown
            if (recv_bytes == 0)
            {
                return new Message(MessageType.DISCONNECT);
            }
            else
            {
                throw new SocketException();
            }
        }


        public virtual void ServiceClient()
        {
            try
            {
                //start the client processing thread, if use specifies to 
                if (useRecvQueue.get())
                    StartReceiving();

                // start the send thread
                if (useSendQueue.get())
                    StartSending();

                // recycle this (current) thread to run the user define AppProc
                AppProc();
            }
            catch (Exception ex)
            {
                Console.WriteLine("TCPResponder::ServiceClient 1 : " + ex.Message);
            }

            try
            {      
                //wait for the receive thread to shutdown
                if (useRecvQueue.get())
                    StopReceiving();
                ShutdownRecv();

                // stop the send thread
                if (useSendQueue.get())
                    StopSending();
                ShutdownSend();

                // signal client to shutdown
                Close();
            }
            catch (Exception ex )
            {
                Console.WriteLine("TCPResponder::ServiceClient 2 : " + ex.Message);
            }
        }
    }
}