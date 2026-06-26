using System;
using System.IO;
using System.Threading;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace LcdFusion
{
    internal sealed class DirectSendResult
    {
        public bool Success;
        public string Message;
    }

    internal static class ValkyrieDirectService
    {
        public const int Width = 320;
        public const int Height = 240;
        // Myth.Cool sends the b5 00 32 refresh roughly every ~38 frames during streaming.
        private const int CommitEveryFrames = 30;
        private static readonly object Sync = new object();
        private static readonly ManualResetEvent FrameSent = new ManualResetEvent(false);
        private static Thread _streamThread;
        private static byte[] _currentFrame;
        private static bool _stopRequested;
        private static string _streamError;

        // bgra: Width*Height*4 bytes, top-down, channel order B,G,R,A.
        public static DirectSendResult ShowBgra(byte[] bgra)
        {
            if (bgra == null || bgra.Length < Width * Height * 4)
                return Fail("Frame Valkyrie non valido.");
            if (VendorService.IsMythCoolRunning())
                return Fail("Chiudi Myth.Cool: sta usando l'interfaccia USB del Valkyrie.");

            try { return Push(BuildFrame(bgra)); }
            catch (Exception ex) { return Fail("Invio Valkyrie fallito: " + ex.Message); }
        }

        public static void Stop()
        {
            Thread thread;
            lock (Sync)
            {
                _stopRequested = true;
                thread = _streamThread;
            }
            if (thread != null && thread != Thread.CurrentThread) thread.Join(4000);
        }

        private static DirectSendResult Push(byte[] frame)
        {
            bool firstStart;
            lock (Sync)
            {
                _currentFrame = frame;
                firstStart = _streamThread == null || !_streamThread.IsAlive;
                if (firstStart)
                {
                    _streamError = null;
                    _stopRequested = false;
                    FrameSent.Reset();
                    _streamThread = new Thread(StreamLoop) { IsBackground = true, Name = "LCD Fusion Valkyrie" };
                    _streamThread.Start();
                }
                else if (!string.IsNullOrEmpty(_streamError))
                {
                    return Fail(_streamError);
                }
            }

            if (!firstStart)
                return new DirectSendResult { Success = true, Message = "Streaming Valkyrie attivo." };

            if (!FrameSent.WaitOne(8000))
                return Fail("Timeout durante l'avvio dello streaming Valkyrie.");
            lock (Sync)
            {
                return string.IsNullOrEmpty(_streamError)
                    ? new DirectSendResult { Success = true, Message = "Streaming Valkyrie attivo." }
                    : Fail(_streamError);
            }
        }

        private static void StreamLoop()
        {
            UsbDevice device = null;
            IUsbDevice wholeDevice = null;
            IntPtr hid = IntPtr.Zero;
            try
            {
                // Full HID init must complete before the first bulk frame. The handle stays
                // open for the lifetime of the stream so we can interleave the b5 00 32 refresh.
                hid = global::ValkyrieHidInitializer.Open();
                global::ValkyrieHidInitializer.RunInit(hid);

                device = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(0x345F, 0x9132));
                if (device == null)
                    throw new InvalidOperationException("Valkyrie non accessibile: " + UsbDevice.LastErrorString);
                wholeDevice = device as IUsbDevice;
                if (wholeDevice != null && !wholeDevice.ClaimInterface(3))
                    throw new InvalidOperationException("Interfaccia Valkyrie occupata: " + UsbDevice.LastErrorString);

                UsbEndpointWriter writer = device.OpenEndpointWriter(WriteEndpointID.Ep04);
                writer.Reset();
                writer.Flush();

                bool firstFrameDone = false;
                long frameIndex = 0;
                while (true)
                {
                    byte[] frame;
                    lock (Sync)
                    {
                        if (_stopRequested) break;
                        frame = _currentFrame;
                    }
                    if (frame == null) { Thread.Sleep(20); continue; }

                    int transferred;
                    ErrorCode code = writer.Write(frame, 2000, out transferred);
                    if (code != ErrorCode.None || transferred != frame.Length)
                        throw new IOException("Errore USB Valkyrie " + code + " (" + transferred + "/" + frame.Length + " byte).");

                    // Mirror Myth.Cool: start scan-out right after the first frame is buffered,
                    // then issue the periodic refresh between subsequent frames.
                    if (!firstFrameDone)
                    {
                        global::ValkyrieHidInitializer.RunPostFrame(hid);
                        firstFrameDone = true;
                    }
                    else if ((frameIndex % CommitEveryFrames) == 0)
                    {
                        global::ValkyrieHidInitializer.Commit(hid);
                    }
                    frameIndex++;

                    FrameSent.Set();
                    Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                lock (Sync) { _streamError = ex.Message; }
                FrameSent.Set();
            }
            finally
            {
                if (wholeDevice != null) try { wholeDevice.ReleaseInterface(3); } catch { }
                if (device != null) try { device.Close(); } catch { }
                if (hid != IntPtr.Zero) try { global::ValkyrieHidInitializer.Close(hid); } catch { }
                lock (Sync)
                {
                    _streamThread = null;
                    _currentFrame = null;
                }
            }
        }

        private static byte[] BuildFrame(byte[] bgra)
        {
            byte[] frame = new byte[8 + Width * Height * 2 + 8];
            byte[] header = { 0xFF, 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0xF0 };
            byte[] footer = { 0xFF, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);

            int destination = 8;
            for (int pixel = 0; pixel < Width * Height; pixel += 2)
            {
                int p0 = pixel * 4;
                int p1 = p0 + 4;
                double b0 = bgra[p0];
                double g0 = bgra[p0 + 1];
                double r0 = bgra[p0 + 2];
                double b1 = bgra[p1];
                double g1 = bgra[p1 + 1];
                double r1 = bgra[p1 + 2];

                byte y0 = Clamp(0.257 * r0 + 0.504 * g0 + 0.098 * b0 + 16.0);
                byte y1 = Clamp(0.257 * r1 + 0.504 * g1 + 0.098 * b1 + 16.0);
                byte u = Clamp((-0.148 * (r0 + r1) - 0.291 * (g0 + g1) + 0.439 * (b0 + b1)) / 2.0 + 128.0);
                byte v = Clamp((0.439 * (r0 + r1) - 0.368 * (g0 + g1) - 0.071 * (b0 + b1)) / 2.0 + 128.0);

                frame[destination++] = u;
                frame[destination++] = y0;
                frame[destination++] = v;
                frame[destination++] = y1;
            }

            Buffer.BlockCopy(footer, 0, frame, destination, footer.Length);
            return frame;
        }

        private static byte Clamp(double value)
        {
            int rounded = (int)Math.Round(value);
            if (rounded < 0) return 0;
            if (rounded > 255) return 255;
            return (byte)rounded;
        }

        private static DirectSendResult Fail(string message)
        {
            return new DirectSendResult { Success = false, Message = message };
        }
    }
}
