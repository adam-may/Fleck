﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
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
        // Maps connection id to real connection
        private readonly IDictionary<Guid, IWebSocketConnection> _connections;
        // Per-user list of prefixes sent to the service
        private readonly IDictionary<Guid, IDictionary<string, string>> _prefixes;
        // List of connection ids for each topic
        private readonly IDictionary<Uri, IList<Guid>> _subscriptions;
        // Lookup for application specific end points. First item in Tuple is the type to be deserialised, 
        // second item is the piece of code to run to handle the call
        private readonly IDictionary<Uri, Tuple<Type, Action<object>>> _registeredDelegates;

        public Action<IWebSocketConnection> OnWelcomeMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnPrefixMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnCallMessage { get; set; }
        public Action<IWebSocketConnection, string, string> OnCallResultMessage { get; set; }
        public Action<IWebSocketConnection, string, string, string, string> OnCallErrorMessage { get; set; }
        public Action<IWebSocketConnection, Uri> OnSubscribeMessage { get; set; }
        public Action<IWebSocketConnection, Uri> OnUnsubscribeMessage { get; set; }
        public Action<IWebSocketConnection, Uri, string, IEnumerable<Guid>, IEnumerable<Guid>> OnPublishMessage { get; set; }
        public Action<IWebSocketConnection, Uri, string> OnEventMessage { get; set; }

        public WampSubProtocolHandler()
        {
            _connections = new Dictionary<Guid, IWebSocketConnection>();
            _prefixes = new Dictionary<Guid, IDictionary<string, string>>();
            _subscriptions = new Dictionary<Uri, IList<Guid>>();
            _registeredDelegates = new Dictionary<Uri, Tuple<Type, Action<object>>>();

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

        #region Dynamic delegates
        public void RegisterDelegateForMessage<T>(Uri uri, Action<T> d)
        {
            _registeredDelegates.Add(uri, Tuple.Create<Type, Action<object>>(typeof(T), o => d((T)o)));
        }

        public void DeregisterDelegateForMessage(Uri uri)
        {
            _registeredDelegates.Remove(uri);
        }
        #endregion

        #region ISubProtocolHandler implementation
        public IDictionary<Guid, IWebSocketConnection> Connections
        {
            get { return _connections; }
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
                        _connections.Add(socket.ConnectionInfo.Id, socket);
                        SendWelcomeMessage(socket);
                    };
                    socket.OnClose = () =>
                    {
                        FleckLog.Debug(String.Format("Removing connection from list: {0}", socket.ConnectionInfo.Id));
                        _connections.Remove(socket.ConnectionInfo.Id);
                    };
                    socket.OnMessage = message =>
                    {
                        FleckLog.Debug(String.Format("Received message from {0}: {1}", socket.ConnectionInfo.Id, message));
                        ParseAndHandleMessage(socket, message);
                    };
                };
            }
        }
        #endregion

        #region Message Senders
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

        public void SendCallResultMessage(IWebSocketConnection connection, string callId, string result)
        {
            object[] parameters = new object[]
            {
                WampMessageTypeId.CallResult,
                callId,
                result
            };
            var callResultMessage = JsonConvert.SerializeObject(parameters);
            connection.Send(callResultMessage);
            FleckLog.Debug(String.Format("Sent CallResult message: {0}", callResultMessage));
            OnCallResultMessage(connection, callId, result);
        }

        public void SendCallErrorMessage(IWebSocketConnection connection, string callId, string errorUri, string errorDescription, string errorDetails = null)
        {
            object[] parameters = null;
            
            if (errorDetails == null)
            {
                parameters = new object[]
                {
                    WampMessageTypeId.CallResult,
                    callId,
                    errorUri,
                    errorDescription
                };
            }
            else
            {
                parameters = new object[]
                {
                    WampMessageTypeId.CallResult,
                    callId,
                    errorUri,
                    errorDescription,
                    errorDetails
                };
            }

            var callErrorMessage = JsonConvert.SerializeObject(parameters);
            connection.Send(callErrorMessage);
            FleckLog.Debug(String.Format("Sent CallError message: {0}", callErrorMessage));
            OnCallErrorMessage(connection, callId, errorUri, errorDescription, errorDetails);
        }

        public void SendEventMessage(IWebSocketConnection connection, string topicUri, string eventId)
        {
            var uri = new Uri(ExpandPrefix(connection, topicUri));

            object[] parameters = new object[]
            {
                WampMessageTypeId.Event,
                uri.ToString(),
                eventId
            };
            var eventMessage = JsonConvert.SerializeObject(parameters);

            if (_subscriptions.ContainsKey(uri))
            {
                _subscriptions[uri].ToList()
                                   .ForEach(guid =>
                                       {
                                           if (_connections.ContainsKey(guid))
                                               _connections[guid].Send(eventMessage);
                                       });
            }
            FleckLog.Debug(String.Format("Sent Event message: {0}", eventMessage));
            OnEventMessage(connection, uri, eventId);
        }
        #endregion

        #region Message Handlers
        private void HandlePrefixMessage(IWebSocketConnection conn, object[] parameters)
        {
            if (parameters.Length != 3)
            {
                FleckLog.Info(String.Format("Received bad prefix message on {0}", conn.ConnectionInfo.Id));
                return;
            }

            var prefix = parameters[1].ToString();
            var uri = parameters[2].ToString();

            if (!_prefixes.ContainsKey(conn.ConnectionInfo.Id))
                _prefixes.Add(conn.ConnectionInfo.Id, new Dictionary<string, string>());

            var _connPrefixes = _prefixes[conn.ConnectionInfo.Id];

            _connPrefixes[prefix] = uri;

            FleckLog.Info(String.Format("Received prefix message on {0}: \"{1}\" -> \"{2}\"", conn.ConnectionInfo.Id, prefix, uri));
            OnPrefixMessage(conn, prefix, uri);
        }

        private void HandleSubscribeMessage(IWebSocketConnection conn, object[] parameters)
        {
            if (parameters.Length != 2)
            {
                FleckLog.Info(String.Format("Received bad subscribe message on {0}", conn.ConnectionInfo.Id));
                return;
            }

            var topicUri = new Uri(ExpandPrefix(conn, parameters[1].ToString()));

            if (!_subscriptions.ContainsKey(topicUri))
                _subscriptions.Add(topicUri, new List<Guid>());

            _subscriptions[topicUri].Add(conn.ConnectionInfo.Id);
            FleckLog.Info(String.Format("Added subscription for topic {0}, connection {1}", topicUri, conn.ConnectionInfo.Id));
            OnSubscribeMessage(conn, topicUri);
        }

        private void HandleUnsubscribeMessage(IWebSocketConnection conn, object[] parameters)
        {
            if (parameters.Length != 2)
            {
                FleckLog.Info(String.Format("Received bad unsubscribe message on {0}", conn.ConnectionInfo.Id));
                return;
            }

            var topicUri = new Uri(ExpandPrefix(conn, parameters[1].ToString()));

            if (!_subscriptions.ContainsKey(topicUri))
                return;

            _subscriptions[topicUri].Remove(conn.ConnectionInfo.Id);
            FleckLog.Info(String.Format("Removed subscription for topic {0}, connection {1}", topicUri, conn.ConnectionInfo.Id));
            OnUnsubscribeMessage(conn, topicUri);

            if (_subscriptions[topicUri].Count() == 0)
            {
                _subscriptions.Remove(topicUri);
                FleckLog.Info(String.Format("Last subscription for topic {0} removed. Removing topic", topicUri));
            }
        }

        private void HandlePublishMessage(IWebSocketConnection conn, object[] parameters)
        {
            if (parameters.Length < 3 || parameters.Length > 5)
            {
                FleckLog.Info(String.Format("Received bad publish message on {0}", conn.ConnectionInfo.Id));
                return;
            }

            var topicUri = new Uri(ExpandPrefix(conn, parameters[1].ToString()));
            var eventId = parameters[2].ToString();

            IEnumerable<Guid> excludeListEnumerable = null;
            IEnumerable<Guid> eligibleListEnumerable = null;
            switch (parameters.Length)
            {
                case 3:
                    excludeListEnumerable = new List<Guid>() { };
                    eligibleListEnumerable = _subscriptions[topicUri];
                    break;
                case 4:
                    excludeListEnumerable = new List<Guid>() { conn.ConnectionInfo.Id };
                    eligibleListEnumerable = _subscriptions[topicUri];
                    break;
                case 5:
                    var excludeList = parameters[3] as JArray;
                    if (excludeList != null)
                    {
                        excludeListEnumerable = excludeList.Select(x => new Guid(x.Value<string>()));
                    }
                    var eligibleList = parameters[4] as JArray;
                    if (eligibleList != null)
                    {
                        eligibleListEnumerable = eligibleList.Select(x => new Guid(x.Value<string>()));
                    }
                    else
                    {
                        eligibleListEnumerable = _subscriptions[topicUri];
                    }
                    break;
            }

            eligibleListEnumerable.Where(guid => !excludeListEnumerable.Contains(guid))
                                  .ToList()
                                  .ForEach(guid =>
                                      {
                                          if (_connections.ContainsKey(guid))
                                          {
                                              var connection = _connections[guid];
                                              // TODO: deserialise message
                                              connection.Send("Publish");
                                          }
                                      });

            FleckLog.Info(String.Format("Published message for topic {0}, event {1}", topicUri, eventId));
            OnPublishMessage(conn, topicUri, eventId, excludeListEnumerable, eligibleListEnumerable);
        }
        #endregion

        #region Utilities
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
                        HandleSubscribeMessage(conn, parsedMessage);
                        break;
                    case WampMessageTypeId.Unsubscribe:
                        // Handle unsubscriptions
                        HandleUnsubscribeMessage(conn, parsedMessage);
                        break;
                    case WampMessageTypeId.Publish:
                        // Handle Publishing of messages
                        HandlePublishMessage(conn, parsedMessage);
                        break;
                    case WampMessageTypeId.Welcome:
                    case WampMessageTypeId.CallResult:
                    case WampMessageTypeId.CallError:
                    case WampMessageTypeId.Event:
                    default:
                        // Shouldn't receive any of these messages
                        FleckLog.Info(String.Format("Received bad message on {0}: {1}", conn.ConnectionInfo.Id, message));
                        break;
                }
            }
        }

        private string ExpandPrefix(IWebSocketConnection conn, string uri)
        {
            // Expands CURIEs (Compact Uris - see http://www.w3.org/TR/curie/) to full Uris
            // This is on a per-connection basis, so we need to use a nested structure
            // First key is connection, second is the CURIE prefix

            Uri uriObject;
            if (Uri.TryCreate(uri, UriKind.Absolute, out uriObject))
                // A regular (non-Compact URI) has been passed in)
                return uri;

            // CURIEs are of the form "prefix:endpoint", where prefix should be replaced with the full endpoint URI
            var components = uri.Split(new [] { ':' });

            if (components.Length != 2)
                return uri;

            var connId = conn.ConnectionInfo.Id;

            if (!_prefixes.ContainsKey(connId) || _prefixes[connId].ContainsKey(components[0]))
                return uri;

            return uri.Replace(components[0] + ":", _prefixes[connId][components[0]]);
        }
        #endregion
    }
}