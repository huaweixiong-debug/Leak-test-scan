using System.Net.Sockets;

namespace ATEQ.LeakTest.Web.Infrastructure;

public class PlcModbusTcpClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ushort _transactionId;

    public bool IsOpen => _tcp?.Connected == true;

    public async Task ConnectAsync(string host, int port, int timeoutMs = 5000)
    {
        await _lock.WaitAsync();
        try
        {
            Disconnect();
            _tcp = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await _tcp.ConnectAsync(host, port, cts.Token);
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = timeoutMs;
            _stream.WriteTimeout = timeoutMs;
        }
        finally { _lock.Release(); }
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _stream = null;
        try { _tcp?.Close(); } catch { /* best-effort */ }
        _tcp = null;
    }

    public void Dispose() => Disconnect();

    // ==================== FC 0x01 Read Coils ====================

    public async Task<bool[]> ReadCoilsAsync(byte unitId, ushort address, ushort count)
    {
        var request = BuildReadCoils(unitId, address, count);
        var (mbapTid, mbapUid, response) = await SendReceiveAsync(request);
        var result = DecodeBitStates(response, 0x01, count);

        // Diagnostic: dump raw PDU bytes and decoded coils
        var pduHex = Convert.ToHexString(response);
        var labels = new List<string>();
        for (int i = 0; i < count; i++)
            labels.Add($"M{i}({address + i})={(result[i] ? "ON" : "OFF")}");
        Console.WriteLine($"[plc] read-coils tid={mbapTid} uid={mbapUid} addr={address} count={count} pdu=[{pduHex}] | {string.Join(" ", labels)}");

        return result;
    }

    // ==================== FC 0x02 Read Discrete Inputs ====================

    public async Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort address, ushort count)
    {
        var request = BuildReadDiscreteInputs(unitId, address, count);
        var (mbapTid, mbapUid, response) = await SendReceiveAsync(request);
        var result = DecodeBitStates(response, 0x02, count);

        var pduHex = Convert.ToHexString(response);
        var labels = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var inputAddress = (ushort)(address + i);
            labels.Add($"X{FormatXLabel(inputAddress)}({inputAddress})={(result[i] ? "ON" : "OFF")}");
        }

        Console.WriteLine($"[plc] read-inputs tid={mbapTid} uid={mbapUid} addr={address} count={count} pdu=[{pduHex}] | {string.Join(" ", labels)}");
        return result;
    }

    // ==================== FC 0x05 Write Single Coil ====================

    public async Task WriteCoilAsync(byte unitId, ushort address, bool value)
    {
        var request = BuildWriteCoil(unitId, address, value);
        var (mbapTid, mbapUid, response) = await SendReceiveAsync(request);
        if (response.Length < 5)
            throw new ModbusException($"Write coil response too short: {response.Length} bytes");
        // FC 0x05 echo: request PDU (bytes 7-11) must match response PDU
        for (int i = 0; i < 5; i++)
        {
            if (request[7 + i] != response[i])
                throw new ModbusException("Write coil response does not match request (echo expected)");
        }

        Console.WriteLine($"[plc] write-coil tid={mbapTid} uid={mbapUid} addr={address} value={(value ? "ON" : "OFF")} echo-ok");
    }

    // ==================== Frame building ====================

    private byte[] BuildReadCoils(byte unitId, ushort address, ushort count)
    {
        var pdu = new byte[5];
        pdu[0] = 0x01;
        pdu[1] = (byte)(address >> 8);
        pdu[2] = (byte)(address & 0xFF);
        pdu[3] = (byte)(count >> 8);
        pdu[4] = (byte)(count & 0xFF);
        return WrapMbap(unitId, pdu);
    }

    private byte[] BuildReadDiscreteInputs(byte unitId, ushort address, ushort count)
    {
        var pdu = new byte[5];
        pdu[0] = 0x02;
        pdu[1] = (byte)(address >> 8);
        pdu[2] = (byte)(address & 0xFF);
        pdu[3] = (byte)(count >> 8);
        pdu[4] = (byte)(count & 0xFF);
        return WrapMbap(unitId, pdu);
    }

    private byte[] BuildWriteCoil(byte unitId, ushort address, bool value)
    {
        var pdu = new byte[5];
        pdu[0] = 0x05;
        pdu[1] = (byte)(address >> 8);
        pdu[2] = (byte)(address & 0xFF);
        pdu[3] = value ? (byte)0xFF : (byte)0x00;
        pdu[4] = 0x00;
        return WrapMbap(unitId, pdu);
    }

    private byte[] WrapMbap(byte unitId, byte[] pdu)
    {
        var tid = unchecked(++_transactionId);
        var length = (ushort)(1 + pdu.Length);
        var frame = new byte[7 + pdu.Length];
        frame[0] = (byte)(tid >> 8);
        frame[1] = (byte)(tid & 0xFF);
        frame[2] = 0x00;
        frame[3] = 0x00;
        frame[4] = (byte)(length >> 8);
        frame[5] = (byte)(length & 0xFF);
        frame[6] = unitId;
        Array.Copy(pdu, 0, frame, 7, pdu.Length);
        return frame;
    }

    // ==================== Send / Receive ====================

    private async Task<(ushort tid, byte uid, byte[] pdu)> SendReceiveAsync(byte[] request)
    {
        await _lock.WaitAsync();
        try
        {
            if (_stream == null || _tcp == null || !_tcp.Connected)
                throw new ModbusException("PLC TCP connection is not open");

            await _stream.WriteAsync(request.AsMemory(0, request.Length));
            await _stream.FlushAsync();

            // MBAP header: TID(2) + PID(2) + Length(2) + UID(1) = 7 bytes
            var header = new byte[7];
            if (await ReadExactAsync(_stream, header, 0, 7) < 7)
                throw new ModbusException("Incomplete MBAP header — connection closed");

            var tid = (ushort)((header[0] << 8) | header[1]);
            var protocolId = (ushort)((header[2] << 8) | header[3]);
            var length = (ushort)((header[4] << 8) | header[5]);
            var uid = header[6];

            if (protocolId != 0)
                throw new ModbusException($"Unexpected protocol ID 0x{protocolId:X4}, expected 0x0000");

            // Standard Modbus TCP: length = unitId(1) + PDU(N)
            // UID already consumed as header[6], so PDU = length - 1
            if (length < 2)
                throw new ModbusException($"Response length too small: {length} (minimum is 2)");
            var pduLength = length - 1;
            if (pduLength <= 0)
                throw new ModbusException($"Invalid PDU length after subtracting UID: {pduLength}");

            var pdu = new byte[pduLength];
            if (await ReadExactAsync(_stream, pdu, 0, pduLength) < pduLength)
                throw new ModbusException($"Incomplete response PDU: expected {pduLength} bytes, connection closed");

            return (tid, uid, pdu);
        }
        finally { _lock.Release(); }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total));
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static bool[] DecodeBitStates(byte[] response, byte expectedFunctionCode, ushort count)
    {
        if (response.Length < 2)
            throw new ModbusException($"Bit-read response too short: {response.Length} bytes");
        if (response[0] != expectedFunctionCode)
            throw new ModbusException($"Unexpected function code 0x{response[0]:X2}, expected 0x{expectedFunctionCode:X2}");

        var result = new bool[count];
        for (int i = 0; i < count; i++)
        {
            var byteIndex = 2 + i / 8;
            var bitIndex = i % 8;
            result[i] = byteIndex < response.Length && (response[byteIndex] & (1 << bitIndex)) != 0;
        }

        return result;
    }

    private static string FormatXLabel(ushort address)
        => address < 8 ? address.ToString() : Convert.ToString(address, 8);
}
