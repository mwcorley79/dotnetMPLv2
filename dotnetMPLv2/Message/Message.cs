using System.Text;

namespace MPL
{
    public enum MessageType : byte
    {
        DEFAULT = 0,
        TEXT = 1,
        REPLY = 2,
        END = 4,
        QUIT = 8,
        DISCONNECT = 16,
        STOP_SENDING = 32,
        STRING = 64,
        BINARY = 128
    }

    // ref: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/how-to-convert-a-byte-array-to-an-int
    // this class serves at the wire format for Message class for C#, for python compatibility,
    // used for transmission over network sockets.
    // the idea is to approximate the Python struct type for packing  Python values represented
    // as Python bytes: https://docs.python.org/3/library/struct

    public class PackedMessage
    {
        const int hdr_len = 5;
        public static int HeaderLen => hdr_len;
        public PackedMessage(byte[] message_content, MessageType msg_type = MessageType.DEFAULT)
        {
            serialized_message_bytes = new byte[message_content.Length + hdr_len];

            // convert the content length into a Uint32 4-byte (byte array (for serializing into a network socket)
            byte[] clen_btyearray = BitConverter.GetBytes((UInt32)message_content.Length);

            // If the system architecture is little endian,
            // then reverse the content len byte array, to begin endian, (network byte order)
            if (BitConverter.IsLittleEndian)
                Array.Reverse(clen_btyearray);

            //first copy the 1-byte message type into the serialized header
            serialized_message_bytes[0] = (byte)msg_type;

            // secondly, copy the big endion 4-byte length content_len array into serialized message  at offset 1
            Buffer.BlockCopy(clen_btyearray, 0, serialized_message_bytes, 1, 4); // *** 4 bytes is the size of unit32

            // lastly, copy the message content array into packed messsage, starting at offset 5 through content len
            Buffer.BlockCopy(message_content, 0, serialized_message_bytes, 5, message_content.Length);
        }

       
        public static Tuple<MessageType, UInt32> ExtractMessageAttribs(byte[] raw_hdr)
        {
            // If the system architecture is little endian,
            // then reverse the content len byte array, to begin endian, (network byte order)
            if (BitConverter.IsLittleEndian)
                Array.Reverse(raw_hdr, 1, 4);  // *** 4 bytes is the size of unit32, starting aat offset 1

            // extract the first byte, which is the message type,
            // and the bytes 1-5 which is the conet size to read out of the socket
            UInt32 content_size_to_read = BitConverter.ToUInt32(new ReadOnlySpan<byte>(raw_hdr, 1, 4));
            return new Tuple<MessageType, UInt32>((MessageType)raw_hdr[0], content_size_to_read);       
        }

        public byte[] GetSerializedMessage
        {
            get { return serialized_message_bytes; }
        }

        private byte[] serialized_message_bytes;
    }



    public class Message
    {
        public MessageType msg_type { get; set; }
        public byte[] Content { get; set; }

        public static byte[] FromString(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        //constructor to construct an empty message
        public Message(MessageType msg_type = MessageType.DEFAULT)
        {
            this.msg_type = msg_type;
            //Content = new byte[] { };
            Content = Array.Empty<byte>();
            
        }

        //constructs a message from bytes
        public Message(byte[] msg_bytes, MessageType msg_type = MessageType.DEFAULT)
        {
            Content = msg_bytes;
            this.msg_type = msg_type;
        }

        public Message(string str, MessageType msg_type = MessageType.DEFAULT) :
                                         this(Encoding.UTF8.GetBytes(str), msg_type)
        { }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(Content);
        }


        public byte[] GetSerializedMessage
        {
            get { return (new PackedMessage(Content, msg_type)).GetSerializedMessage; }
        }

#if TEST_MSG
        public static void DisplayMessageData(Message msg)
        {
            Console.WriteLine($"Packed (binary) message (in hex): { Convert.ToHexString(msg.GetSerializedMessage)}\n");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("*** Testing Message class operations *** ");

            Console.WriteLine("Constructing a binary message object (bmsg1)...");
            Message bmsg1 = new Message(new byte[] { (byte)'a', (byte)'b', (byte)'c' });
            Console.WriteLine($"\"bmsg1\" identity : {bmsg1.GetHashCode()}");
            Console.WriteLine($"\"bmsg1\" data     : {bmsg1}");
            Console.WriteLine($"\"bmsg1\" len      : {bmsg1.Content.Length}");
            Console.WriteLine($"\"bmsg1\" type     : {bmsg1.msg_type}");

            var msg_content = bmsg1.Content; // uses a property
            Console.WriteLine($"\"bmsg1\" testing (byte) content accessor property:-> {Encoding.UTF8.GetString(bmsg1.Content)} -> {msg_content.GetType().Name}");

            var msg_content_str = bmsg1.ToString();
            Console.WriteLine($"\"bmsg1\" testing (string) content accessor property:-> {msg_content_str} -> {msg_content_str.GetType().Name}");

            Console.WriteLine("Display \"bmsg1\" binary (network byte order) packed data");
            Message.DisplayMessageData(bmsg1);


            Console.WriteLine("Constructing a String (str) message object (strmsg1), encoded as utf-8");
            Message strmsg1 = new Message("string message 1", MessageType.STRING);
            Console.WriteLine($"\"strmsg1\" identity : {strmsg1.GetHashCode()}");
            Console.WriteLine($"\"strmsg1\" data     : {strmsg1}");
            Console.WriteLine($"\"strmsg1\" len      : {strmsg1.Content.Length}");
            Console.WriteLine($"\"strmsg1\" type     : {strmsg1.msg_type}");
            Console.WriteLine($"Display \"strmsg1\" binary (network byte order) packed data");
            Message.DisplayMessageData(strmsg1);

            Console.WriteLine("\nTesting \"content\" and \"msg_type\" property \"strmsg1\"");
            strmsg1.Content = Message.FromString("abcde");
            strmsg1.msg_type = MessageType.TEXT;
            Console.WriteLine($"\"strmsg1\" identity : {strmsg1.GetHashCode()}");
            Console.WriteLine($"\"strmsg1\" data     : {strmsg1}");
            Console.WriteLine($"\"strmsg1\" len      : {strmsg1.Content.Length}");
            Console.WriteLine($"\"strmsg1\" type     : {strmsg1.msg_type}");
            Console.WriteLine($"Display \"strmsg1\" binary (network byte order) packed data");
            Message.DisplayMessageData(strmsg1);

            //testing = operator
            Message strmsg2 = strmsg1;
            Console.WriteLine("testing assignment operator (strmsg2 = strmsg1): ** Notice: id (identity doesn't change, indicating a reference, not a copy **");
            Console.WriteLine($"\"strmsg2\" identity : {strmsg2.GetHashCode()}");
            Console.WriteLine($"\"strmsg2\" data     : {strmsg2}");
            Console.WriteLine($"\"strmsg2\" len      : {strmsg2.Content.Length}");
            Console.WriteLine($"\"strmsg2\" type     : {strmsg1.msg_type}");
            Console.WriteLine($"Display \"strmsg2\" binary (network byte order) packed data");
            Message.DisplayMessageData(strmsg2);


            Console.Write("testing empty message");
            Message emptyMsg1 = new Message();
            Console.WriteLine($"\"emptyMsg1\" identity : {emptyMsg1.GetHashCode()}");
            Console.WriteLine($"\"emptyMsg1\" data     : {emptyMsg1}");
            Console.WriteLine($"\"emptyMsg1\" len      : {emptyMsg1.Content.Length}");
            Console.WriteLine($"\"emptyMsg1\" type     : {emptyMsg1.msg_type}");
            Console.WriteLine($"Display \"emptyMsg1\" binary (network byte order) packed data");
            Message.DisplayMessageData(emptyMsg1);

            Console.WriteLine("testing empty message # 2");
            Message emptyMsg2 = new (MessageType.DISCONNECT);
            Console.WriteLine("testing empty2 message");
            Console.WriteLine($"\"emptyMsg2\" identity : {emptyMsg2.GetHashCode()}");
            Console.WriteLine($"\"emptyMsg2\" data     : {emptyMsg2}");
            Console.WriteLine($"\"emptyMsg2\" len      : {emptyMsg2.Content.Length}");
            Console.WriteLine($"\"emptyMsg2\" type     : {emptyMsg2.msg_type}");
            Console.WriteLine($"Display \"emptyMsg2\" binary (network byte order) packed data");
            Message.DisplayMessageData(emptyMsg2);

            // test property accessor 
            Console.WriteLine("testing \"GetSerializedMessage\" property accessor");
            Message msg = new Message("this is message for testing the property/accessor", MessageType.STRING);

            // test serialized binary (packed) message for transmission over socket
            Console.WriteLine($"The message is: {msg}");
            byte[] data = msg.GetSerializedMessage;
            Console.WriteLine($"\"msg\"  -> serialized message data is: {Convert.ToHexString(data)}");
            Console.WriteLine($"\"msg\" len is: {data.Length}");
            Console.WriteLine($"Content len : {msg.Content.Length}");

            Console.WriteLine("test message header extraction method: \"ExtractMessageAttribs\"");
            var vals = PackedMessage.ExtractMessageAttribs(data);
            Console.WriteLine($"Message type: {vals.Item1}");
            Console.WriteLine($"Content len : {vals.Item2}");
        }  
#endif
    }
}