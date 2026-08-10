using FrostySdk.IO;
using FrostySdk.Managers;

namespace Frosty.Core.Mod
{
    public class ResResource : BaseModResource
    {
        public override ModResourceType Type => ModResourceType.Res;
        public uint ResType => resType;

        protected uint resType;
        protected ulong resRid;
        protected byte[] resMeta;

        public ResResource()
        {
        }

        internal ResResource(ResAssetEntry entry)
            : base(entry)
        {
        }

        public override void Read(NativeReader reader)
        {
            base.Read(reader);

            resType = reader.ReadUInt();
            resRid = reader.ReadULong();
            resMeta = reader.ReadBytes(reader.ReadInt());

            // @todo: prevent loading of data that requires a handler but has not been exported with said handler
            //        the same goes for the other types

            //foreach (var attr in Assembly.GetExecutingAssembly().GetCustomAttributes<ResCustomHandlerAttribute>())
            //{
            //    if ((uint)attr.ResType == resType && handlerHash == 0)
            //    {
            //        // block all res files that require a custom handler when one is not specified
            //        throw new FrostyModLoadException("Failed to load a resource that requires a custom handler, due to a null handler found");
            //    }
            //}
        }

        public override void FillAssetEntry(object entry)
        {
            base.FillAssetEntry(entry);
            ResAssetEntry resEntry = entry as ResAssetEntry;

            resEntry.ResType = resType;
            resEntry.ResRid = resRid;
            resEntry.ResMeta = resMeta;
        }

        internal override void WriteCopy(NativeWriter writer, int newResourceIndex)
        {
            base.WriteCopy(writer, newResourceIndex);
            writer.Write(resType);
            writer.Write(resRid);
            writer.Write(resMeta?.Length ?? 0);
            if (resMeta != null) writer.Write(resMeta);
        }
    }
}
