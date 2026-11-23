using System.Drawing;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Microsoft.Win32;

namespace LegacyShop.Data
{
    public class SnapshotStore
    {
        public byte[] Save(object snapshot)
        {
            var formatter = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                formatter.Serialize(ms, snapshot);
                return ms.ToArray();
            }
        }

        public object Load(byte[] data)
        {
            var formatter = new BinaryFormatter();
            using (var ms = new MemoryStream(data))
            {
                return formatter.Deserialize(ms);
            }
        }

        public string GetInstallPath()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\LegacyShop"))
            {
                return key?.GetValue("InstallPath") as string;
            }
        }

        public Bitmap Thumbnail(string path) => new Bitmap(path);
    }
}
