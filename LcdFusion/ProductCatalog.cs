namespace LcdFusion
{
    // Identity + panel specs for a supported LCD. New products from the same brands plug
    // in here: add an entry. If a product shares an existing protocol (same framing/format,
    // different VID/PID or resolution) it can reuse that direct service with parameters; a
    // genuinely new protocol needs its own *DirectService implementation.
    internal sealed class LcdProduct
    {
        public string Brand;      // e.g. "Valkyrie / Myth.Cool", "Thermalright / TRCC"
        public string Model;      // human-readable model name
        public ushort Vid;
        public ushort Pid;
        public int Width;
        public int Height;
        public string Protocol;   // "valkyrie-hid-uyvy" | "thermalright-winusb-jpeg"
        public bool Implemented;  // driven by an existing DirectService today
    }

    internal static class ProductCatalog
    {
        public static readonly LcdProduct[] Products =
        {
            new LcdProduct
            {
                Brand = "Valkyrie / Myth.Cool", Model = "Valkyrie AIO LCD",
                Vid = 0x345F, Pid = 0x9132, Width = 320, Height = 240,
                Protocol = "valkyrie-hid-uyvy", Implemented = true
            },
            new LcdProduct
            {
                Brand = "Thermalright / TRCC", Model = "Thermalright AIO LCD",
                Vid = 0x0416, Pid = 0x5408, Width = 1920, Height = 462,
                Protocol = "thermalright-winusb-jpeg", Implemented = true
            },
            // Future products (same brands) go here, e.g. other Valkyrie/Thermalright panels.
        };

        public static LcdProduct Find(ushort vid, ushort pid)
        {
            foreach (LcdProduct p in Products)
                if (p.Vid == vid && p.Pid == pid) return p;
            return null;
        }

        public static LcdProduct Valkyrie { get { return Products[0]; } }
        public static LcdProduct Thermalright { get { return Products[1]; } }
    }
}
