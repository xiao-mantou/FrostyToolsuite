using FrostySdk.IO;
using FrostySdk.Managers;

namespace Frosty.Core.Mod
{
    public sealed class BundleResource : BaseModResource
    {
        public override ModResourceType Type => ModResourceType.Bundle;
        private int superBundleName;

        public BundleResource()
        {
        }

        public override void Read(NativeReader reader)
        {
            base.Read(reader);
            name = reader.ReadNullTerminatedString();
            superBundleName = reader.ReadInt();
        }

        public override void FillAssetEntry(object entry)
        {
            BundleEntry bentry = entry as BundleEntry;
            bentry.Name = name;
            bentry.SuperBundleId = superBundleName;
        }

        internal override void WriteCopy(NativeWriter writer, int newResourceIndex)
        {
            base.WriteCopy(writer, newResourceIndex);
            writer.WriteNullTerminatedString(name ?? "");
            writer.Write(superBundleName);
        }
    }
}
