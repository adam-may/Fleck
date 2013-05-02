using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Fleck.Wamp.Tests
{
    [TestFixture]
    public class WampSubProtocolHandlerTests
    {
        private Mock<IWebSocketServer> _webSocketServer;
        private IEnumerable<ISubProtocolHandler> _subProtocolInitializers;
        private WampSubProtocolHandler _wampSubProtocolHandler;

        [SetUp]
        public void Setup()
        {
            _wampSubProtocolHandler = new WampSubProtocolHandler();

            _webSocketServer = new Mock<IWebSocketServer>("ws://localhost:8080");
            _webSocketServer.Setup(
                x => x.Start(It.IsAny<ISubProtocolHandler>(), It.IsAny<IEnumerable<ISubProtocolHandler>>()))
                            .Returns(x => x);
            _subProtocolInitializers = new List<ISubProtocolHandler>() {_wampSubProtocolHandler};
        }

        [Test]
        public void ShouldReturnProperIdentifier()
        {
            Assert.IsTrue(_wampSubProtocolHandler.Identifier.Equals("wamp"));
        }

        [Test]
        public void ShouldCallOnWelcome()
        {
            _webSocketServer.Start(_wampSubProtocolHandler, _subProtocolInitializers);
            
        }
    }
}
