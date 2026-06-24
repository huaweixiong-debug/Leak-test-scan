using System.IO.Ports;

namespace ATEQ.LeakTest.Web.Infrastructure;

/// <summary>
/// Lightweight Modbus RTU frame builder / parser over System.IO.Ports.
/// </summary>
public class ModbusRtuClient : IDisposable
{
    private SerialPort? _port;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsOpen => _port?.IsOpen == true;

    public async Task ConnectAsync(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits, int timeoutMs, bool dtr = true, bool rts = true)
    {
        await _lock.WaitAsync();
        try
        {
            Close();
            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = timeoutMs,
                WriteTimeout = timeoutMs,
                DtrEnable = dtr,
                RtsEnable = rts
            };
            _port.Open();
        }
        finally { _lock.Release(); }
    }

    public void Close()
    {
        if (_port == null) return;
        try { if (_port.IsOpen) _port.Close(); }
        catch { /* ignore */ }
        _port.Dispose();
        _port = null;
    }

    public void Dispose() => Close();

    // ==================== Modbus operations ====================

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort address, ushort count)
    {
        var request = BuildReadHoldingRegisters(slaveId, address, count);
        var response = await SendReceiveAsync(request, 5 + count * 2);
        if (response[1] != 0x03) throw new ModbusException("Unexpected function code in response");
        var byteCount = response[2];
        var result = new ushort[byteCount / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
        return result;
    }

    public async Task WriteRegisterAsync(byte slaveId, ushort address, ushort value)
    {
        var request = BuildWriteRegister(slaveId, address, value);
        await SendReceiveAsync(request, 8);
    }

    public async Task WriteRegistersAsync(byte slaveId, ushort address, ushort[] values)
    {
        var request = BuildWriteRegisters(slaveId, address, values);
        await SendReceiveAsync(request, 8);
    }

    public async Task WriteCoilAsync(byte slaveId, ushort address, bool value)
    {
        var request = BuildWriteCoil(slaveId, address, value);
        await SendReceiveAsync(request, 8);
    }

    // ==================== Frame building ====================

    private static byte[] BuildReadHoldingRegisters(byte slaveId, ushort address, ushort count) =>
        WithCrc([slaveId, 0x03, (byte)(address >> 8), (byte)(address & 0xff), (byte)(count >> 8), (byte)(count & 0xff)]);

    private static byte[] BuildWriteRegister(byte slaveId, ushort address, ushort value) =>
        WithCrc([slaveId, 0x06, (byte)(address >> 8), (byte)(address & 0xff), (byte)(value >> 8), (byte)(value & 0xff)]);

    private static byte[] BuildWriteRegisters(byte slaveId, ushort address, ushort[] values)
    {
        var frame = new List<byte>
        {
            slaveId, 0x10, (byte)(address >> 8), (byte)(address & 0xff),
            (byte)(values.Length >> 8), (byte)(values.Length & 0xff),
            (byte)(values.Length * 2)
        };
        foreach (var v in values) { frame.Add((byte)(v >> 8)); frame.Add((byte)(v & 0xff)); }
        return WithCrc(frame.ToArray());
    }

    private static byte[] BuildWriteCoil(byte slaveId, ushort address, bool value) =>
        WithCrc([slaveId, 0x05, (byte)(address >> 8), (byte)(address & 0xff), value ? (byte)0xff : (byte)0x00, (byte)0x00]);

    // ==================== CRC16 ====================

    private static byte[] WithCrc(byte[] data)
    {
        var crc = Crc16(data);
        var result = new byte[data.Length + 2];
        Array.Copy(data, result, data.Length);
        result[^2] = (byte)(crc & 0xff);
        result[^1] = (byte)(crc >> 8);
        return result;
    }

    private static ushort Crc16(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                else crc >>= 1;
            }
        }
        return crc;
    }

    // ==================== Send / Receive ====================

    private async Task<byte[]> SendReceiveAsync(byte[] request, int expectedMinLength)
    {
        await _lock.WaitAsync();
        try
        {
            if (_port == null || !_port.IsOpen) throw new ModbusException("Serial port is not open");

            // Clear buffers
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            _port.Write(request, 0, request.Length);

            // Read response: 1 byte slave ID + 1 byte function code + ... + 2 bytes CRC
            await Task.Delay(20); // Wait for device to process
            var buffer = new byte[256];
            var totalRead = 0;
            var deadline = DateTime.UtcNow.AddMilliseconds(_port.ReadTimeout);

            while (totalRead < expectedMinLength && DateTime.UtcNow < deadline)
            {
                try
                {
                    if (_port.BytesToRead > 0)
                    {
                        var read = _port.Read(buffer, totalRead, buffer.Length - totalRead);
                        totalRead += read;
                    }
                    else
                    {
                        await Task.Delay(5);
                    }
                }
                catch (TimeoutException)
                {
                    break;
                }
            }

            if (totalRead < expectedMinLength)
                throw new ModbusException($"Modbus response too short: expected {expectedMinLength}, got {totalRead}");

            var response = new byte[totalRead];
            Array.Copy(buffer, response, totalRead);

            // Verify CRC
            if (totalRead >= 4)
            {
                var expectedCrc = Crc16(response.Take(totalRead - 2).ToArray());
                var receivedCrc = (ushort)(response[totalRead - 2] | (response[totalRead - 1] << 8));
                if (expectedCrc != receivedCrc)
                    throw new ModbusException($"CRC mismatch: expected 0x{expectedCrc:X4}, got 0x{receivedCrc:X4}");
            }

            return response;
        }
        finally { _lock.Release(); }
    }
}

public class ModbusException : Exception
{
    public ModbusException(string message) : base(message) { }
    public ModbusException(string message, Exception? inner) : base(message, inner) { }
}
