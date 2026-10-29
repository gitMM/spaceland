using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum PerceptionMode { Passive, InspectionOnly }
    public sealed class PerceptionRule
    {
        public SubjectKind Subject { get; }
        public AttributeKind Attribute { get; }
        public PerceptionMode Mode { get; }
        public PerceptionRule(SubjectKind subject, AttributeKind attribute, PerceptionMode mode)
        { if (!Enum.IsDefined(typeof(SubjectKind), subject) || !Enum.IsDefined(typeof(AttributeKind), attribute) || !Enum.IsDefined(typeof(PerceptionMode), mode)) throw new ArgumentException("Invalid perception rule."); Subject = subject; Attribute = attribute; Mode = mode; }
    }
    public sealed class PerceptionCatalog
    {
        private readonly SortedDictionary<(SubjectKind, AttributeKind), PerceptionRule> rules = new SortedDictionary<(SubjectKind, AttributeKind), PerceptionRule>();
        public IReadOnlyList<PerceptionRule> Rules { get; }
        public PerceptionCatalog(IEnumerable<PerceptionRule>? overrides = null)
        {
            foreach (var attribute in new[] { AttributeKind.Location, AttributeKind.TraversalTicks }) Add(SubjectKind.Tile, attribute, PerceptionMode.Passive);
            foreach (var attribute in new[] { AttributeKind.Location, AttributeKind.Resource, AttributeKind.Quantity, AttributeKind.Present, AttributeKind.LocationKind, AttributeKind.LocationOwner }) Add(SubjectKind.Item, attribute, PerceptionMode.Passive);
            Add(SubjectKind.Machine, AttributeKind.Location, PerceptionMode.Passive); Add(SubjectKind.Machine, AttributeKind.Operational, PerceptionMode.Passive);
            Add(SubjectKind.Machine, AttributeKind.RequiredResource, PerceptionMode.InspectionOnly); Add(SubjectKind.Machine, AttributeKind.InstalledResource, PerceptionMode.InspectionOnly);
            Add(SubjectKind.Actor, AttributeKind.Location, PerceptionMode.InspectionOnly);
            Add(SubjectKind.Tile, AttributeKind.Dirt, PerceptionMode.Passive);
            var seen = new SortedSet<(SubjectKind, AttributeKind)>();
            foreach (var rule in overrides ?? Array.Empty<PerceptionRule>())
            {
                var key = (rule.Subject, rule.Attribute);
                if (!rules.ContainsKey(key) || !seen.Add(key)) throw new ArgumentException("Unsupported or duplicate perception fact type.");
                rules[key] = rule;
            }
            Rules = Array.AsReadOnly(rules.Values.ToArray());
        }
        private void Add(SubjectKind subject, AttributeKind attribute, PerceptionMode mode) => rules.Add((subject, attribute), new PerceptionRule(subject, attribute, mode));
        public PerceptionMode Mode(SubjectKind subject, AttributeKind attribute) => rules.TryGetValue((subject, attribute), out var rule) ? rule.Mode : throw new ArgumentException("Undeclared perception fact type.");
    }
}
