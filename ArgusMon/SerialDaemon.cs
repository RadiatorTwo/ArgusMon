using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace ArgusMon;

public class SerialDaemon
{
    private SerialPort _port;
    private readonly string _device;
    private readonly int _baudRate;
    private Thread _workerThread;
    private volatile bool _running = true;

    public SerialDaemon(string device, int baudRate)
    {
        _device = device;
        _baudRate = baudRate;
    }

    public void Start()
    {
        _workerThread = new Thread(Worker);
        _workerThread.IsBackground = true;
        _workerThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _port?.Close();
    }

    private void Worker()
    {
        while (_running)
        {
            try
            {
                Connect();

                while (_running && _port.IsOpen)
                {
                    // (1) Sende Anfrage
                    byte[] request = new byte[] { 0xAA, 0x02, 0x20 };
                    byte crc = CalculateCRC8(request);
                    byte[] fullRequest = new byte[4];
                    Array.Copy(request, fullRequest, 3);
                    fullRequest[3] = crc;

                    _port.Write(fullRequest, 0, 4);

                    // (2) Lese Antwort (mit Timeout)
                    Thread.Sleep(100); // kurze Wartezeit
                    byte[] buffer = new byte[256];
                    int len = _port.Read(buffer, 0, buffer.Length);
                    if (len > 0)
                    {
                        ProcessPacket(buffer, len);
                    }

                    // (3) Warte 10 Sekunden
                    for (int i = 0; i < 10 && _running; i++)
                        Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.Message}");
                Thread.Sleep(5000); // kurz warten, bevor man reconnectet
            }
        }
    }

    private void Connect()
    {
        if (_port != null && _port.IsOpen)
        {
            _port.Close();
        }

        _port = new SerialPort(_device, _baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000
        };

        _port.Open();
        Log($"[INFO] Port {_device} geöffnet.");
        Thread.Sleep(2000); // Geräte-Reset-Zeit
    }

    private void ProcessPacket(byte[] buffer, int length)
    {
        if (length < 6)
        {
            Log("[WARN] Paket zu kurz");
            return;
        }

        byte receivedCRC = buffer[length - 1];
        byte calculatedCRC = CalculateCRC8(buffer[..(length - 1)]);

        if (receivedCRC != calculatedCRC)
        {
            Log($"[WARN] CRC Fehler: empfangen 0x{receivedCRC:X2}, erwartet 0x{calculatedCRC:X2}");
            return;
        }

        if (buffer[2] != 0x20)
        {
            Log($"[WARN] Drittes Byte ist nicht 0x20: 0x{buffer[2]:X2}");
            return;
        }

        ushort temp = (ushort)((buffer[4] << 8) | buffer[5]);
        int tempTimes100 = temp * 100;

        File.WriteAllText("/var/log/argus_temp.out", $"{tempTimes100}\n");
        Log($"[INFO] Temperatur: {tempTimes100}");
    }

    private byte CalculateCRC8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x01) != 0 ? (byte)((crc >> 1) ^ 0x8C) : (byte)(crc >> 1);
        }
        return crc;
    }

    private void Log(string message)
    {
        Console.WriteLine($"{DateTime.Now:O} {message}");
        // Alternativ mit syslog über native call oder eigene Datei
    }
}
