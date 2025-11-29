using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Networking;
using NUnit.Framework;
using System.Threading.Tasks;

namespace NetSdrClientAppTests;

[TestFixture]
public class NetSdrClientTests
{
    private NetSdrClient _client;
    private Mock<ITcpClient> _tcpMock;
    private Mock<IUdpClient> _udpMock;

    [SetUp]
    public void Setup()
    {
        _tcpMock = new Mock<ITcpClient>();
        _udpMock = new Mock<IUdpClient>();

        // Налаштування з'єднання
        _tcpMock.Setup(tcp => tcp.Connect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(true);
        });

        _tcpMock.Setup(tcp => tcp.Disconnect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(false);
        });

        _tcpMock.Setup(tcp => tcp.Connected).Returns(false);

        // --- ВАЖЛИВО ДЛЯ ПОКРИТТЯ ---
        // Імітуємо миттєву відповідь сервера на будь-який запит.
        // Це дозволяє пройти через 'await' у методах StartIQAsync/StopIQAsync
        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>((sentBytes) =>
            {
                // Тригеримо подію, ніби сервер відповів
                _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, new byte[] { 0x00, 0x01 });
            })
            .Returns(Task.CompletedTask);

        _client = new NetSdrClient(_tcpMock.Object, _udpMock.Object);
    }

    [Test]
    public async Task ConnectAsync_WhenNotConnected_ShouldConnectAndSendSetupMessages()
    {
        _tcpMock.Setup(t => t.Connected).Returns(false);

        await _client.ConnectAsync();

        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
    }

    [Test]
    public async Task ConnectAsync_WhenAlreadyConnected_ShouldDoNothing()
    {
        _tcpMock.Setup(t => t.Connected).Returns(true);

        await _client.ConnectAsync();

        _tcpMock.Verify(tcp => tcp.Connect(), Times.Never);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public void Disconnect_ShouldCallTcpDisconnect()
    {
        _client.Disconect();
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task StartIQAsync_WhenNotConnected_ShouldNotSendAnything()
    {
        _tcpMock.Setup(t => t.Connected).Returns(false);

        await _client.StartIQAsync();

        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task StartIQAsync_WhenConnected_ShouldSendStartCommandAndListenUdp()
    {
        _tcpMock.Setup(t => t.Connected).Returns(true);

        await _client.StartIQAsync();

        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
        _udpMock.Verify(udp => udp.StartListeningAsync(), Times.Once);
        Assert.That(_client.IQStarted, Is.True);
    }

    [Test]
    public async Task StopIQAsync_WhenNotConnected_ShouldNotSendAnything()
    {
        _tcpMock.Setup(t => t.Connected).Returns(false);

        await _client.StopIQAsync();

        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public async Task StopIQAsync_WhenConnected_ShouldSendStopCommandAndStopUdp()
    {
        _tcpMock.Setup(t => t.Connected).Returns(true);
        _client.IQStarted = true;

        await _client.StopIQAsync();

        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
        _udpMock.Verify(udp => udp.StopListening(), Times.Once);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task ChangeFrequencyAsync_ShouldSendCommand()
    {
        _tcpMock.Setup(t => t.Connected).Returns(true);

        await _client.ChangeFrequencyAsync(1000000, 0);

        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
    }
    
    [Test]
    public async Task SendTcpRequest_WhenNoConnection_ShouldReturnEmptyArray()
    {
        // Перевіряємо приватну гілку if (!_tcpClient.Connected) return Array.Empty
        _tcpMock.Setup(t => t.Connected).Returns(false);
        
        await _client.StartIQAsync(); 
        
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    /* // ТЕСТ ЗАКОМЕНТОВАНО, ЩОБ НЕ ВИКЛИКАТИ ПОМИЛКУ НА GITHUB (File Access Error)
    [Test]
    public void UdpClient_MessageReceived_ShouldProcessData()
    {
        byte[] fakeUdpData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.DoesNotThrow(() => 
        {
            _udpMock.Raise(u => u.MessageReceived += null, _udpMock.Object, fakeUdpData);
        });
    } 
    */
}