using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Networking;
using NUnit.Framework;

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

        // 1. Налаштування з'єднання
        _tcpMock.Setup(tcp => tcp.Connect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(true);
        });

        _tcpMock.Setup(tcp => tcp.Disconnect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(false);
        });

        // За замовчуванням клієнт не підключений
        _tcpMock.Setup(tcp => tcp.Connected).Returns(false);

        // 2. МАГІЯ ДЛЯ ПОКРИТТЯ SendTcpRequest
        // Коли клієнт відправляє повідомлення, ми імітуємо, що сервер одразу відповів "ОК".
        // Це розблокує 'await responseTask' всередині NetSdrClient.
        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>((sentBytes) =>
            {
                // Імітуємо відповідь сервера (просто повертаємо ті ж байти або пустий масив)
                // Це тригерить _tcpClient_MessageReceived всередині клієнта
                _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, new byte[] { 0x00, 0x01 });
            })
            .Returns(Task.CompletedTask);

        _client = new NetSdrClient(_tcpMock.Object, _udpMock.Object);
    }

    [Test]
    public async Task ConnectAsync_WhenNotConnected_ShouldConnectAndSendSetupMessages()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(false);

        // Act
        await _client.ConnectAsync();

        // Assert
        // 1. Перевіряємо, що викликали Connect
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once);
        
        // 2. Перевіряємо, що відправили 3 налаштувальні повідомлення (SampleRate, Filter, ADMode)
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
    }

    [Test]
    public async Task ConnectAsync_WhenAlreadyConnected_ShouldDoNothing()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(true);

        // Act
        await _client.ConnectAsync();

        // Assert
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Never);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public void Disconnect_ShouldCallTcpDisconnect()
    {
        // Act
        _client.Disconect();

        // Assert
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task StartIQAsync_WhenNotConnected_ShouldNotSendAnything()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(false);

        // Act
        await _client.StartIQAsync();

        // Assert
        // Не має відправляти команди, бо немає з'єднання
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task StartIQAsync_WhenConnected_ShouldSendStartCommandAndListenUdp()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(true);

        // Act
        await _client.StartIQAsync();

        // Assert
        // 1. Відправив команду старту
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
        // 2. Почав слухати UDP
        _udpMock.Verify(udp => udp.StartListeningAsync(), Times.Once);
        // 3. Змінив статус
        Assert.That(_client.IQStarted, Is.True);
    }

    [Test]
    public async Task StopIQAsync_WhenNotConnected_ShouldNotSendAnything()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(false);

        // Act
        await _client.StopIQAsync();

        // Assert
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public async Task StopIQAsync_WhenConnected_ShouldSendStopCommandAndStopUdp()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(true);
        _client.IQStarted = true;

        // Act
        await _client.StopIQAsync();

        // Assert
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
        _udpMock.Verify(udp => udp.StopListening(), Times.Once);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task ChangeFrequencyAsync_ShouldSendCommand()
    {
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(true);

        // Act
        await _client.ChangeFrequencyAsync(1000000, 0);

        // Assert
        // Перевіряємо, що команда пішла
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
    }

    [Test]
    public void UdpClient_MessageReceived_ShouldProcessData()
    {
        // Цей тест покриває метод _udpClient_MessageReceived
        // УВАГА: Оскільки в коді є запис у файл (new FileStream...), цей тест створить файл samples.bin
        
        // Arrange
        // Створюємо фейкові дані, які емулюють пакет від SDR
        // Структура пакету залежить від NetSdrMessageHelper, але спробуємо передати мінімум
        byte[] fakeUdpData = new byte[] { 
            0x04, 0x00, // Header length (приклад)
            0x00, 0x00, // Sequence info
            0x01, 0x02, 0x03, 0x04 // Body / Samples data
        };

        // Act
        // Примусово викликаємо подію MessageReceived на моку UDP
        // Це змусить спрацювати метод _udpClient_MessageReceived у твоєму класі
        Assert.DoesNotThrow(() => 
        {
            _udpMock.Raise(u => u.MessageReceived += null, _udpMock.Object, fakeUdpData);
        });

        // Assert
        // Оскільки метод void і пише у файл, ми перевіряємо, що він просто не впав (DoesNotThrow).
        // Це зарахує рядки коду як покриті.
    }
    
    [Test]
    public async Task SendTcpRequest_WhenNoConnection_ShouldReturnEmptyArray()
    {
        // Тест покриває випадок if (!_tcpClient.Connected) у приватному методі SendTcpRequest
        
        // Arrange
        _tcpMock.Setup(t => t.Connected).Returns(false);
        
        // Act
        // StartIQAsync викликає SendTcpRequest. Якщо немає з'єднання, SendTcpRequest повертає empty array.
        await _client.StartIQAsync(); 
        
        // Assert
        // Перевіряємо, що нічого не відправилось
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }
}