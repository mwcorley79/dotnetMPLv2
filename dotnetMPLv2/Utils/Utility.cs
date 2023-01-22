using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MPL
{
    public class AtomicBool
    {
        Object obj;
        volatile bool val;
       
        public AtomicBool(bool v=false)
        {
          obj = new object();
          val = v;
        }

        public bool get()
        {
            lock(obj)
            {
                return val;
            }
        }

        public void set(bool value)
        {
            lock(obj)
            {
                val = value;
            }
        }
    }

    public class Atomic<T>
    {
        Object obj;
        T val;

        public Atomic(T v)
        {
            obj = new object();
            val = v;
        }

        public T get()
        {
            lock (obj)
            {
                return val;
            }
        }

        public void set(T value)
        {
            lock (obj)
            {
                val = value;
            }
        }
    }

    public static class SocketUtils
    {
        public static int RecvAll(Socket sock, byte[] buf, SocketFlags flags)
        {
            int bytesRecvd, bytesLeft = buf.Length;
            int blockIndx = 0;

            while (bytesLeft > 0)
            {
                bytesRecvd = sock.Receive(buf, blockIndx, bytesLeft, flags);

                if (bytesRecvd > 0)
                {
                    bytesLeft -= bytesRecvd;
                    blockIndx += bytesRecvd;
                }
                else
                    return bytesRecvd;
            }
            return buf.Length;
        }

        public static int SendAll(Socket sock, byte[] buf, SocketFlags flags)
        {
            int bytesSent, bytesLeft = buf.Length;
            int blockIndx = 0;

            while (bytesLeft > 0)
            {
                bytesSent = sock.Send(buf, blockIndx, bytesLeft, flags);
                if (bytesSent > 0)
                {
                    bytesLeft -= bytesSent;
                    blockIndx += bytesSent;
                }
                else
                    return bytesSent;

            }
            return buf.Length;
        }
    }
}
