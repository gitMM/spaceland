using System.Collections.Generic;
using System.IO;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteStrings(BinaryWriter w, IReadOnlyList<string> values)
        { w.Write(values.Count); foreach (string value in values) w.Write(value); }
        private static List<string> ReadStrings(BinaryReader r)
        { int count = Count(r); var values = new List<string>(); for (int i = 0; i < count; i++) values.Add(r.ReadString()); return values; }
        private static void WritePublicWork(BinaryWriter w, PublicWorkCatalog? catalog)
        {
            w.Write(catalog != null); if (catalog == null) return;
            w.Write(catalog.PickupTicks);
            w.Write(catalog.Resources.Count); foreach (var x in catalog.Resources) { w.Write(x.Id); w.Write(x.FitClass); w.Write(x.Rating); }
            w.Write(catalog.Recipes.Count);
            foreach (var x in catalog.Recipes) { w.Write(x.Id); w.Write(x.Capability); w.Write(x.Duration); WriteAmounts(w, x.Inputs); WriteAmounts(w, x.Outputs); }
            w.Write(catalog.Workstations.Count);
            foreach (var x in catalog.Workstations) { w.Write(x.Id); WriteCoord(w, x.Position); w.Write(x.Capability); w.Write(x.ToolResource); WriteStrings(w, x.Roles); w.Write(x.RequiresCredential); }
            w.Write(catalog.Equipment.Count);
            foreach (var x in catalog.Equipment) { w.Write(x.Id); WriteCoord(w, x.Position); w.Write(x.FitClass); w.Write(x.ToolResource); WriteStrings(w, x.Roles); w.Write(x.InstallTicks); w.Write(x.InspectTicks); w.Write(x.RequiresCredential); }
            w.Write(catalog.Cleanable.Count);
            foreach (var x in catalog.Cleanable) { w.Write(x.Tile.Value); WriteCoord(w, x.Position); w.Write(x.UnitTicks); w.Write(x.ToolResource); WriteStrings(w, x.Roles); }
        }
        private static PublicWorkCatalog? ReadPublicWork(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            long pickup = r.ReadInt64(); int count = Count(r); var resources = new List<ResourceDefinition>();
            for (int i = 0; i < count; i++) resources.Add(new ResourceDefinition(r.ReadString(), r.ReadString(), r.ReadInt32()));
            count = Count(r); var recipes = new List<RecipeDefinition>();
            for (int i = 0; i < count; i++) { long id = r.ReadInt64(); string capability = r.ReadString(); long duration = r.ReadInt64(); recipes.Add(new RecipeDefinition(id, capability, ReadAmounts(r), ReadAmounts(r), duration)); }
            count = Count(r); var stations = new List<PublicWorkstation>();
            for (int i = 0; i < count; i++) stations.Add(new PublicWorkstation(r.ReadInt64(), ReadCoord(r), r.ReadString(), r.ReadString(), ReadStrings(r), r.ReadBoolean()));
            count = Count(r); var equipment = new List<PublicEquipment>();
            for (int i = 0; i < count; i++) equipment.Add(new PublicEquipment(r.ReadInt64(), ReadCoord(r), r.ReadString(), r.ReadString(), ReadStrings(r), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean()));
            count = Count(r); var cleanable = new List<PublicCleanable>();
            for (int i = 0; i < count; i++) cleanable.Add(new PublicCleanable(new Id<TileTag>(r.ReadInt64()), ReadCoord(r), r.ReadInt64(), r.ReadString(), ReadStrings(r)));
            return new PublicWorkCatalog(resources, recipes, stations, equipment, pickup, cleanable);
        }
    }
}
