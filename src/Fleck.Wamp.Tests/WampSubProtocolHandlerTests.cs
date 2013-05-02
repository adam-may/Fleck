using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Fleck.Wamp.Tests
{
    [TestFixture]
    public class WampSubProtocolHandlerTests
    {
        private Guid _connectionId = new Guid("CFCAB391-2567-4DDE-844F-C44231CA8605");
        private string _applicationIdentifier = "Fleck.Wamp/0.9.6";

        private Mock<IWebSocketConnectionInfo> _webSocketConnectionInfo;
        private Mock<IWebSocketConnection> _webSocketConnection;
        private WampSubProtocolHandler _wampSubProtocolHandler;

        [SetUp]
        public void Setup()
        {
            _webSocketConnectionInfo = new Mock<IWebSocketConnectionInfo>();
            _webSocketConnectionInfo.SetupGet(x => x.Id).Returns(_connectionId);
            _webSocketConnection = new Mock<IWebSocketConnection>();
            _webSocketConnection.SetupGet(x => x.ConnectionInfo).Returns(_webSocketConnectionInfo.Object);
            _wampSubProtocolHandler = new WampSubProtocolHandler();
        }

        [Test]
        public void ShouldReturnProperIdentifier()
        {
            // Assert
            Assert.IsTrue(_wampSubProtocolHandler.Identifier.Equals("wamp"));
        }

        [Test]
        public void ShouldCallOnWelcomeWhenClientConnects()
        {
            // Arrange
            var welcomeCalled = false;
            string message = string.Empty;
            string expectedWelcomeMessage = String.Format("[0,\"{0}\",1,\"{1}\"]", _connectionId, _applicationIdentifier);

            _webSocketConnection.SetupAllProperties();
            _webSocketConnection.Setup(x => x.Send(It.IsAny<string>())).Callback<String>(m => message = m);

            _wampSubProtocolHandler.OnWelcomeMessage += conn => { welcomeCalled = true; };
            _wampSubProtocolHandler.SubProtocolInitializer(_webSocketConnection.Object);

            // Act
            _webSocketConnection.Object.OnOpen();

            // Assert
            Assert.IsTrue(welcomeCalled);
            Assert.IsTrue(message.Equals(expectedWelcomeMessage));
        }

        [Test]
        public void ShouldManipulateCollectionCorrectlyWhenClientConnectsOrDisconnects()
        {
            // Arrange
            _webSocketConnection.SetupAllProperties();
            _wampSubProtocolHandler.SubProtocolInitializer(_webSocketConnection.Object);

            // Act
            _webSocketConnection.Object.OnOpen();

            // Assert
            Assert.IsTrue(_wampSubProtocolHandler.Connections.Count == 1);
            Assert.IsTrue(_wampSubProtocolHandler.Connections.ContainsKey(_connectionId));

            // Act
            _webSocketConnection.Object.OnClose();

            // Assert
            Assert.IsTrue(_wampSubProtocolHandler.Connections.Count == 0);
        }

        [Test]
        public void ShouldAddPrefixToCollection()
        {
            // Arrange
            var prefixCalled = false;
            var intendedPrefix = "keyvalue";
            var intendedUri = "http://example.com/simple/keyvalue#";
            var prefixMessage = String.Format("[1, \"{0}\", \"{1}\"]", intendedPrefix, intendedUri);
            var returnedPrefix = String.Empty;
            var returnedUri = String.Empty;

            _webSocketConnection.SetupAllProperties();
            _wampSubProtocolHandler.SubProtocolInitializer(_webSocketConnection.Object);

            _webSocketConnection.Object.OnOpen();

            _wampSubProtocolHandler.OnPrefixMessage += (conn, prefix, uri) =>
                {
                    prefixCalled = true;
                    returnedPrefix = prefix;
                    returnedUri = uri;
                };

            // Act
            _webSocketConnection.Object.OnMessage(prefixMessage);

            // Assert
            Assert.IsTrue(prefixCalled);
            Assert.IsTrue(returnedPrefix.Equals(intendedPrefix));
            Assert.IsTrue(returnedUri.Equals(intendedUri));
        }
    }
}
