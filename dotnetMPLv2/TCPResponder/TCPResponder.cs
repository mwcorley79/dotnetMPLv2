using SWTools;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MPL
{
    public class DefaultClientHandler : ClientHandler
    {
        // this is a creational function: you must implement it
        // the TCPResponder creates an instance of this class (it's how the server polymorphism works)
        public override ClientHandler Clone()
        {
            return new DefaultClientHandler();
        }

        // this is where you define the custom server processing: you must implement it
        public override void AppProc()
        {
            Message msg;
            // no use of queue
            // while ((msg = ReceiveMessage()).msg_type != MessageType.DISCONNECT)

            while ((msg = GetMessage()).msg_type != MessageType.DISCONNECT)     
            {
                Console.WriteLine($"From Client: {RemoteEP} -> {msg}");

                Console.WriteLine($"Sending echo reply: {msg}");
                PostMessage(msg);
            }
        }
    };

    public class TCPResponder
    {
        private Socket listenSocket_;
        private EndPoint listen_ep_;
        //private Task listenTask;
        private Thread listenThread;
        private AtomicBool isListening;
   
        private ClientHandler ch_;
        private Atomic<int> numClients;

        public TCPResponder(EndPoint ep)
        {
            listenSocket_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listen_ep_ = ep;
            listenSocket_.Bind(listen_ep_);
            // service_queue = new BlockingQueue<Task<bool>>();
 
            isListening = new AtomicBool(false);
            numClients = new Atomic<int>(-1);
            ch_ = new DefaultClientHandler();
        }

      
        public void UseQueues(bool val)
        {
            ch_.UseQueues(val);
        }

        public int NumClients
        {
            get
            {
                return numClients.get();
            }
            set
            {
                numClients.set(value);
            }
        }

        public void RegisterClientHandler(ClientHandler ch)
        {
            ch_ = ch;
        }


        public void Start(int backlog=20)
        {
            if (!isListening.get())
            {
                isListening.set(true);

                //listenTask = Task.Run(() =>
                listenThread = new Thread( () =>
               {
                   int client_count = 0;
                   listenSocket_.Listen(backlog);
                   List<Thread> serviceQ_ = new List<Thread>();
                   Console.WriteLine($"Listening on: {listen_ep_.ToString()}");
                   while (isListening.get() && (client_count++ < NumClients || NumClients == -1))
                   {
                       Socket client_socket = listenSocket_.Accept();

                       if (client_socket != null)
                       {
                           if (ch_ != null)
                           {
                               Console.WriteLine($"Serving Client: {client_count}");
                               ClientHandler ch = ch_.Clone();

                               ch.SetSocket(client_socket);

                               ch.SetServiceEndPoint(listen_ep_);

                               // same principle effect as Dr. Fawcett's C++ ThreadPool
                               // (see the C++ version, TCPResponder.cpp)
                               // each client is service on a separate thread pool task (a C# future)
                               //here we use the blocking queue to signal when to exit;

                               /* service_queue.enQ(Task.Run(() =>
                               {
                                   ServiceClient(ch);
                                   return true;
                               }));
                               */

                               // wait for all service threads to complete 
                               //Thread serviceClient = new Thread(() => ServiceClient(ch));
                               Thread serviceClient = new Thread(() => ch.ServiceClient());
                               serviceQ_.Add(serviceClient);
                               serviceClient.Start();
                           }
                       }
                   }

                // wait for all service tasks to complete 
                for (int i = 0; i < serviceQ_.Count; ++i)
                       serviceQ_[i].Join();


                // use to null to signal complete 
                /* service_queue.enQ(Task.Run(() => false));
                Task<bool> client_task;
                
                // wait for all service threads to complete 
                do
                {
                    client_task = service_queue.deQ();
                    client_task.Wait();
                }
                while (client_task.Result == true);
                */

               });
                listenThread.Start();
            }
        }

        public void Stop()
        {
            // if user started the listener, then shut it all down
            if (isListening.get())
            {
                try
                {
                    //listenTask.Wait();
                    listenThread.Join();
                   
                    listenSocket_.Close();
                    isListening.set(false);
                }
                catch
                {
                    isListening.set(false);
                }
            }
        }


        public static void Main()
        {
            const int NUM_CLIENTS = 100;
          
            EndPoint addr = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8080);
            DefaultClientHandler ph = new DefaultClientHandler();

            // define instance of the TCPResponder (server host)
            TCPResponder responder = new TCPResponder(addr);

            // set number of clients for the server process to service before exiting (-1 runs indefinitely)
            responder.NumClients = NUM_CLIENTS;

            // responder.UseQueues(true);  // uncomment if you use SendMessage/ReceiveMessage

            // register the custom client handler with TCPResponder instance
            responder.RegisterClientHandler(ph);

            // start the server listening thread
            responder.Start();

           
            responder.Stop();
        }

        
    }
}
