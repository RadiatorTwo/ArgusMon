using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace ArgusMon;

public class SerialDaemon(string device, int baudRate)
{
    private SerialPort _port;
    private Thread _workerThread;
    private volatile bool _running = true;

    public void Start()
    {
        _workerThread = new Thread(Worker)
        {
            IsBackground = true
        };
        
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
                    var request = new byte[] { 0xAA, 0x02, 0x20 };
                    var crc = CalculateCrc8(request);
                    var fullRequest = new byte[4];
                    Array.Copy(request, fullRequest, 3);
                    fullRequest[3] = crc;

                    _port.Write(fullRequest, 0, 4);

                    // (2) Lese Antwort (mit Timeout)
                    Thread.Sleep(100); // kurze Wartezeit
                    var buffer = new byte[256];
                    var len = _port.Read(buffer, 0, buffer.Length);
                    if (len > 0)
                    {
                        ProcessPacket(buffer, len);
                    }

                    // (3) Warte 10 Sekunden
                    for (var i = 0; i < 10 && _running; i++)
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
        if (_port is { IsOpen: true })
        {
            _port.Close();
        }

        _port = new SerialPort(device, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000
        };

        _port.Open();
        Log($"[INFO] Port {device} geöffnet.");
        Thread.Sleep(2000); // Geräte-Reset-Zeit
    }

    private void ProcessPacket(byte[] buffer, int length)
    {
        if (length < 13)
        {
            Log("[WARN] Paket zu kurz");
            return;
        }

        var receivedCrc = buffer[12];
        var calculatedCrc = CalculateCrc8(buffer.AsSpan(0, 12)); // Bytes 0 bis 11
        
        if (receivedCrc != calculatedCrc)
        {
            Log($"[WARN] CRC Fehler: empfangen 0x{receivedCrc:X2}, erwartet 0x{calculatedCrc:X2}");
            return;
        }

        if (buffer[2] != 0x20)
        {
            Log($"[WARN] Drittes Byte ist nicht 0x20: 0x{buffer[2]:X2}");
            return;
        }

        int tempCount = buffer[3]; // Sollte 4 sein
        if (tempCount != 4)
        {
            Log($"[WARN] Unerwartete TEMP_COUNT: {tempCount}");
            return;
        }
        
        var temp0 = (ushort)((buffer[4] << 8) | buffer[5]);
        // var temp1 = (ushort)((buffer[6] << 8) | buffer[7]);
        // var temp2 = (ushort)((buffer[8] << 8) | buffer[9]);
        // var temp3 = (ushort)((buffer[10] << 8) | buffer[11]);
        
        var temp0Times100 = temp0 * 100;
        // int temp1Times100 = temp1 * 100;
        // int temp2Times100 = temp2 * 100;
        // int temp3Times100 = temp3 * 100;

        File.WriteAllText("/var/tmp/argus_temp.out", $"{temp0Times100}\n");
        Log($"[INFO] Temperatur: {temp0Times100}");
    }

    private static byte CalculateCrc8(ReadOnlySpan<byte> data)
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

    private static void Log(string message)
    {
        Console.WriteLine($"{DateTime.Now:O} {message}");
        // Alternativ mit syslog über native call oder eigene Datei
    }
}
