using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Serialization;

namespace Fleck
{
    public enum WampMessageTypeId
    {
        Welcome = 0,
        Prefix,
        Call,
        CallResult,
        CallError,
        Subscribe,
        Unsubscribe,
        Publish,
        Event
    }

    public class WampSubProtocolHandler : ISubProtocolHandler
    {
        private const int PROTOCOL_VERSION = 1;
        private readonly string _serverIdentity;
        private readonly List<IWebSocketConnection> _connections;
        private readonly IDictionary<string, IDictionary<string, string>> _prefixes;

        public Action<IWebSocketConnection> OnWelcomeMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnPrefixMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnCallMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnCallResultMessage { get; set; }
        public Action<IWebSocketConnection, string, string, string, string> OnCallErrorMessage { get; set; }
        public Action<IWebSocketConnection, string> OnSubscribeMessage { get; set; }
        public Action<IWebSocketConnection, string> OnUnsubscribeMessage { get; set; }
        public Action<IWebSocketConnection, string, string, IEnumerable<string>, IEnumerable<string>> OnPublishMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnEventMessage { get; set; }
        
        public IEnumerable<IWebSocketConnection> Connections
        {
            get { return _connections; }
        }

        public WampSubProtocolHandler()
        {
            _connections = new List<IWebSocketConnection>();
            _prefixes = new Dictionary<string, IDictionary<string, string>>();

            var assemblyName = Assembly.GetExecutingAssembly().GetName();
            _serverIdentity = String.Format("{0}/{1}.{2}.{3}",
                assemblyName.Name,
                assemblyName.Version.Major,
                assemblyName.Version.Minor,
                assemblyName.Version.Build);

            OnWelcomeMessage = conn => { };
            OnPrefixMessage = (conn, prefix, uri) => { };
            OnCallMessage = (conn, callId, procUri) => { };
            OnCallResultMessage = (conn, callId, result) => { };
            OnCallErrorMessage = (conn, callId, errorUri, errorDesc, errorDetails) => { };
            OnSubscribeMessage = (conn, topicUri) => { };
            OnUnsubscribeMessage = (conn, topicUri) => { };
            OnPublishMessage = (conn, topicUri, eventId, exclude, eligible) => { };
            OnEventMessage = (conn, topicUri, eventId) => { };
        }

        public string Identifier
        {
            get { return "wamp"; }
        }

        public Action<IWebSocketConnection> SubProtocolInitializer
        {
            get
            {
                return socket =>
                {
                    socket.OnOpen = () =>
                    {
                        FleckLog.Debug(String.Format("Adding connection to list: {0}", socket.ConnectionInfo.Id));
                        _connections.Add(socket);
                        SendWelcomeMessage(socket);
                    };
                    socket.OnClose = () =>
                    {
                        FleckLog.Debug(String.Format("Removing connection from list: {0}", socket.ConnectionInfo.Id));
                        _connections.RemoveAll(conn => conn.ConnectionInfo.Id == socket.ConnectionInfo.Id);
                    };
                    socket.OnMessage = message =>
                    {
                        FleckLog.Debug(String.Format("Received message from {0}: {1}", socket.ConnectionInfo.Id, message));
                        ParseAndHandleMessage(socket, message);
                    };
                };
            }
        }

        private void SendWelcomeMessage(IWebSocketConnection connection)
        {
            object[] parameters = new object[]
            {
                WampMessageTypeId.Welcome,
                connection.ConnectionInfo.Id,
                PROTOCOL_VERSION,
                _serverIdentity
            };
            var welcomeMessage = JsonConvert.SerializeObject(parameters);
            connection.Send(welcomeMessage);
            FleckLog.Debug(String.Format("Sent Welcome message: {0}", welcomeMessage));
            OnWelcomeMessage(connection);
        }

        private void ParseAndHandleMessage(IWebSocketConnection conn, string message)
        {
            var parsedMessage = JsonConvert.DeserializeObject<object[]>(message);
            WampMessageTypeId messageType;
            
            
            if (Enum.TryParse<WampMessageTypeId>(parsedMessage[0].ToString(), out messageType))
            {
                switch (messageType)
                {
                    case WampMessageTypeId.Prefix:
                        // Handle prefix message
                        HandlePrefixMessage(conn, parsedMessage);
                        break;
                    case WampMessageTypeId.Call:
                        // Handle call message
                        break;
                    case WampMessageTypeId.Subscribe:
                        // Handle subscriptions
                        break;
                    case WampMessageTypeId.Unsubscribe:
                        // Handle unsubscriptions
                        break;
                    case WampMessageTypeId.Publish:
                        // Handle Publishing of messages
                        break;
                    case WampMessageTypeId.Event:
                        // Handle sending of events 
                        break;
                    case WampMessageTypeId.Welcome:
                    case WampMessageTypeId.CallResult:
                    case WampMessageTypeId.CallError:
                    default:
                        // Shouldn't receive any of these messages
                        FleckLog.Info(String.Format("Received bad message on {0}: {1}", conn.ConnectionInfo.Id, message));
                        break;
                }
            }
        }

        private void HandlePrefixMessage(IWebSocketConnection conn, object[] parameters)
        {
            if (parameters.Length != 3)
            {
                FleckLog.Info(String.Format("Received bad prefix message on {0}", conn.ConnectionInfo.Id));
                return;
            }

            var prefix = parameters[1].ToString();
            var uri = parameters[2].ToString();

            if (!_prefixes.ContainsKey(conn.ConnectionInfo.Id.ToString()))
                _prefixes.Add(conn.ConnectionInfo.Id.ToString(), new Dictionary<string, string>());

            var _connPrefixes = _prefixes[conn.ConnectionInfo.Id.ToString()];

            _connPrefixes[prefix] = uri;

            FleckLog.Info(String.Format("Received prefix message on {0}: \"{1}\" -> \"{2}\"", conn.ConnectionInfo.Id, prefix, uri));
            OnPrefixMessage(conn, prefix, uri);
        }
    }
}
