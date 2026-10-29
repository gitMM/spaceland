using System;
using System.IO;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private static void WriteConstructionOrder(BinaryWriter w, ConstructionOrder c)
        {
            w.Write(c.ProjectId); w.Write(c.TileId); w.Write(c.DefinitionId); WriteCoord(w, c.Site); w.Write(c.Recipe.Id); w.Write(c.Recipe.Capability); w.Write(c.Recipe.Duration); WriteGrowthMaterials(w, c.Recipe.Inputs);
            w.Write(c.Roles.Count); foreach (string role in c.Roles) w.Write(role); w.Write(c.ToolResource); w.Write(c.RequiresCredential); w.Write(c.Berths);
        }
        private static WorkOrderPayload ReadConstructionWorkOrder(BinaryReader r, long target, long expires)
        {
            if (target != 0) throw new ArgumentException("Construction order has an unrelated target.");
            long project = r.ReadInt64(), tile = r.ReadInt64(); string definition = r.ReadString(); var site = ReadCoord(r); long recipe = r.ReadInt64(); string capability = r.ReadString(); long duration = r.ReadInt64();
            var inputs = ReadGrowthMaterials(r); int count = Count(r); var roles = new string[count]; for (int i = 0; i < count; i++) roles[i] = r.ReadString();
            return new WorkOrderPayload(new ConstructionOrder(project, tile, definition, site, new RecipeDefinition(recipe, capability, inputs, Array.Empty<MaterialAmount>(), duration), roles, r.ReadString(), r.ReadBoolean(), r.ReadInt64()), expires);
        }
    }
}
