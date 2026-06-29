using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace LcdFusion
{
    internal static class ThermalrightDirectService
    {
        public const int CanvasWidth = 1920;
        public const int CanvasHeight = 462;
        private static readonly object Sync = new object();
        private static readonly ManualResetEvent FrameSent = new ManualResetEvent(false);
        private static readonly AutoResetEvent WakeStream = new AutoResetEvent(false);
        private static Thread _streamThread;
        private static byte[] _currentPackets;
        private static bool _stopRequested;
        private static string _streamError;

        // bitmap is expected to already be CanvasWidth x CanvasHeight.
        public static DirectSendResult ShowBitmap(Bitmap bitmap)
        {
            if (bitmap == null) return Fail("Frame Thermalright non valido.");
            if (VendorService.IsTrccRunning())
                return Fail("Chiudi TRCC e i suoi helper: stanno usando il Thermalright.");

            try { return Push(Packetize(ToJpeg(bitmap))); }
            catch (Exception ex) { return Fail("Invio Thermalright fallito: " + ex.Message); }
        }

        public static void Stop()
        {
            Thread thread;
            lock (Sync)
            {
                _stopRequested = true;
                thread = _streamThread;
            }
            WakeStream.Set();
            if (thread != null && thread != Thread.CurrentThread) thread.Join(4000);
        }

        private static DirectSendResult Push(byte[] packets)
        {
            bool firstStart;
            lock (Sync)
            {
                _currentPackets = packets;
                firstStart = _streamThread == null || !_streamThread.IsAlive;
                if (firstStart)
                {
                    _streamError = null;
                    _stopRequested = false;
                    FrameSent.Reset();
                    _streamThread = new Thread(StreamLoop) { IsBackground = true, Name = "LCD Fusion Thermalright" };
                    _streamThread.Start();
                }
                else if (!string.IsNullOrEmpty(_streamError))
                {
                    return Fail(_streamError);
                }
            }
            WakeStream.Set();

            if (!firstStart)
                return new DirectSendResult(true, "Streaming Thermalright attivo.");

            if (!FrameSent.WaitOne(6000))
                return Fail("Timeout durante l'avvio dello streaming Thermalright.");
            lock (Sync)
            {
                return string.IsNullOrEmpty(_streamError)
                    ? new DirectSendResult(true, "Streaming Thermalright attivo.")
                    : Fail(_streamError);
            }
        }

        private static void StreamLoop()
        {
            UsbDevice device = null;
            try
            {
                device = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(0x0416, 0x5408));
                if (device == null)
                    throw new InvalidOperationException("Thermalright non accessibile: " + UsbDevice.LastErrorString);

                UsbEndpointWriter writer = device.OpenEndpointWriter(WriteEndpointID.Ep09);
                UsbEndpointReader reader = device.OpenEndpointReader(ReadEndpointID.Ep01, 512);
                byte[] init = new byte[2048];
                init[0] = 0x02;
                init[1] = 0xFF;

                DirectSendResult transfer = WriteBlock(writer, init);
                if (!transfer.Success) throw new IOException(transfer.Message);
                transfer = ReadAck(reader, "handshake");
                if (!transfer.Success) throw new IOException(transfer.Message);

                while (true)
                {
                    byte[] packets;
                    lock (Sync)
                    {
                        if (_stopRequested) break;
                        packets = _currentPackets;
                    }
                    if (packets == null) { WakeStream.WaitOne(250); continue; }

                    for (int offset = 0; offset < packets.Length; offset += 4096)
                    {
                        int length = Math.Min(4096, packets.Length - offset);
                        int transferred;
                        ErrorCode code = writer.Write(packets, offset, length, 1000, out transferred);
                        if (code != ErrorCode.None || transferred != length)
                            throw new IOException("Errore USB Thermalright " + code + " (" + transferred + "/" + length + ").");
                    }
                    transfer = ReadAck(reader, "frame");
                    if (!transfer.Success) throw new IOException(transfer.Message);
                    FrameSent.Set();
                    // TRCC streams the panel at a steady ~156 ms cadence (~6.4 fps). Re-send the
                    // current frame at that rate so the refresh frequency matches the original.
                    WakeStream.WaitOne(150);
                }
            }
            catch (Exception ex)
            {
                lock (Sync) { _streamError = ex.Message; }
                FrameSent.Set();
            }
            finally
            {
                if (device != null) try { device.Close(); } catch { }
                lock (Sync)
                {
                    _streamThread = null;
                    _currentPackets = null;
                }
            }
        }

        private static byte[] ToJpeg(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ImageCodecInfo encoder = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.FormatID == ImageFormat.Jpeg.Guid) encoder = codec;

                if (encoder == null)
                {
                    bitmap.Save(stream, ImageFormat.Jpeg);
                }
                else
                {
                    using (EncoderParameters parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                        bitmap.Save(stream, encoder, parameters);
                    }
                }
                return stream.ToArray();
            }
        }

        private static byte[] Packetize(byte[] jpeg)
        {
            const int payloadSize = 496;
            const int packetSize = 512;
            int packetCount = jpeg.Length / payloadSize + 1;
            int paddedCount = (packetCount + 3) / 4 * 4;
            int remainder = jpeg.Length % payloadSize;
            byte[] result = new byte[paddedCount * packetSize];

            for (int packet = 0; packet < packetCount; packet++)
            {
                int destination = packet * packetSize;
                int length = packet == packetCount - 1 ? remainder : payloadSize;
                result[destination] = 0x01;
                result[destination + 1] = 0xFF;
                Put32(result, destination + 2, jpeg.Length);
                Put16(result, destination + 6, length);
                result[destination + 8] = 0x01;
                Put16(result, destination + 9, packetCount);
                Put16(result, destination + 11, packet);
                if (length > 0)
                    Buffer.BlockCopy(jpeg, packet * payloadSize, result, destination + 16, length);
            }
            return result;
        }

        private static DirectSendResult WriteBlock(UsbEndpointWriter writer, byte[] data)
        {
            int transferred;
            ErrorCode code = writer.Write(data, 1000, out transferred);
            return code == ErrorCode.None && transferred == data.Length
                ? new DirectSendResult(true, "")
                : Fail("Handshake Thermalright fallito: " + code + " (" + transferred + ").");
        }

        private static DirectSendResult ReadAck(UsbEndpointReader reader, string phase)
        {
            byte[] reply = new byte[512];
            int transferred;
            ErrorCode code = reader.Read(reply, 500, out transferred);
            if (code != ErrorCode.None || transferred < 2 || reply[0] != 0x03 || reply[1] != 0xFF)
                return Fail("Risposta Thermalright non valida durante " + phase + ": " + code + ".");
            return new DirectSendResult(true, "");
        }

        private static void Put16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
        }

        private static void Put32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        private static DirectSendResult Fail(string message)
        {
            return new DirectSendResult(false, message);
        }
    }
}
