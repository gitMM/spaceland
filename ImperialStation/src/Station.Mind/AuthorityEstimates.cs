using System;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public static class AuthorityEstimates
    {
        public static WorkDurationEstimate? Construction(IAuthorityView view, long actorId, ConstructionOrder project, long acknowledgmentTerminal)
        {
            var person = view.Personnel.SingleOrDefault(p => p.ActorId == actorId);
            var origin = view.Publication.Services.SingleOrDefault(t => t.Id == acknowledgmentTerminal);
            if (person == null || origin == null || view.Publication.Work == null) return null;
            var target = view.Publication.Services.Where(t => (t.Capabilities & TerminalCapability.Post) != 0 && t.PermittedRoles.Contains(person.Role, StringComparer.Ordinal))
                .Select(t => (Terminal: t, Cost: TravelEstimates.Authority(project.Site, t.Position, view.NominalTravelTicks))).OrderBy(t => t.Cost).ThenBy(t => t.Terminal.Id).FirstOrDefault();
            if (target.Terminal == null) return null;
            long travel = TravelEstimates.Authority(origin.Position, project.Site, view.NominalTravelTicks);
            long work = checked(project.Recipe.Duration + project.Recipe.Inputs.Count * view.Publication.Work.PickupTicks);
            return new WorkDurationEstimate(travel, work, target.Cost, target.Terminal.Id, checked(travel + work + target.Cost + view.LoginTicks + view.PostTicks + view.LatencyTicks));
        }
        public static WorkDurationEstimate? Cleanup(IAuthorityView view, long actorId, Id<TileTag> tile, long reportedUnits, long inspectTicks, long acknowledgmentTerminal)
        {
            if (reportedUnits < 0 || inspectTicks < 1) throw new ArgumentException("Invalid reported cleanup workload.");
            var person = view.Personnel.SingleOrDefault(p => p.ActorId == actorId);
            var site = view.Publication.Work?.Cleanable.SingleOrDefault(c => c.Tile == tile);
            var origin = view.Publication.Services.SingleOrDefault(t => t.Id == acknowledgmentTerminal);
            if (person == null || site == null || origin == null) return null;
            var target = view.Publication.Services.Where(t => (t.Capabilities & TerminalCapability.Post) != 0 && t.PermittedRoles.Contains(person.Role, StringComparer.Ordinal))
                .Select(t => (Terminal: t, Cost: TravelEstimates.Authority(site.Position, t.Position, view.NominalTravelTicks)))
                .OrderBy(t => t.Cost).ThenBy(t => t.Terminal.Id).FirstOrDefault();
            if (target.Terminal == null) return null;
            long travel = TravelEstimates.Authority(origin.Position, site.Position, view.NominalTravelTicks);
            long work = checked(inspectTicks + checked(site.UnitTicks * reportedUnits));
            long total = checked(travel + work + target.Cost + view.LoginTicks + view.PostTicks + view.LatencyTicks);
            return new WorkDurationEstimate(travel, work, target.Cost, target.Terminal.Id, total);
        }
        public static WorkDurationEstimate? Repair(IAuthorityView view, long actorId, long machineId, long acknowledgmentTerminal, bool verification = false)
        {
            var person = view.Personnel.SingleOrDefault(p => p.ActorId == actorId);
            var machine = view.Publication.Work?.Equipment.SingleOrDefault(m => m.Id == machineId);
            var origin = view.Publication.Services.SingleOrDefault(t => t.Id == acknowledgmentTerminal);
            if (person == null || machine == null || origin == null) return null;
            var target = view.Publication.Services.Where(t => (t.Capabilities & TerminalCapability.Post) != 0 && t.PermittedRoles.Contains(person.Role, StringComparer.Ordinal))
                .Select(t => (Terminal: t, Cost: TravelEstimates.Authority(machine.Position, t.Position, view.NominalTravelTicks)))
                .OrderBy(t => t.Cost).ThenBy(t => t.Terminal.Id).FirstOrDefault();
            if (target.Terminal == null) return null;
            long travel = TravelEstimates.Authority(origin.Position, machine.Position, view.NominalTravelTicks);
            long work = verification ? machine.InspectTicks : machine.InstallTicks;
            long total = checked(travel + work + target.Cost + view.LoginTicks + view.PostTicks + view.LatencyTicks);
            return new WorkDurationEstimate(travel, work, target.Cost, target.Terminal.Id, total);
        }
        public static long OffDutyExtension(long receipt, long baseDeadline, RosterPayload? roster, long checkInWindow)
        {
            if (receipt < 0 || baseDeadline < receipt || checkInWindow < 0) throw new ArgumentException("Invalid deadline interval.");
            if (roster == null) return 0;
            long time = receipt % roster.DayTicks;
            long relative = time >= roster.ShiftStart ? time - roster.ShiftStart : roster.DayTicks - (roster.ShiftStart - time);
            long knownShiftEnd = checked(receipt - relative + roster.DutyTicks);
            // The specification adds one whole known off-duty gap and the check-in
            // window. Do not infer actual attendance or repeatedly stretch a deadline.
            return baseDeadline > knownShiftEnd ? checked(roster.DayTicks - roster.DutyTicks + checkInWindow) : 0;
        }
        public static ReportDeadline Deadline(long receipt, long estimate, long learnedRatio, AuthorityConfig policy, RosterPayload? roster, long checkInWindow)
        {
            if (receipt < 0 || estimate < 1 || learnedRatio < 0) throw new ArgumentException("Invalid estimate or learned ratio.");
            long slack = Math.Max(policy.SlackMinimum, Math.Min(policy.SlackMaximum, checked(learnedRatio + policy.SlackMargin)));
            long product = checked(estimate * slack);
            long scaled = checked(product / 1000 + (product % 1000 == 0 ? 0 : 1));
            long baseTick = checked(receipt + scaled + policy.GraceTicks);
            return new ReportDeadline(slack, baseTick, OffDutyExtension(receipt, baseTick, roster, checkInWindow));
        }
        public static long Learn(long prior, long acknowledgmentReceipt, long resultReceipt, long estimate, AuthorityConfig policy)
        {
            if (prior < 0 || acknowledgmentReceipt < 0 || resultReceipt < acknowledgmentReceipt || estimate < 1) throw new ArgumentException("Invalid received duration sample.");
            long sample = checked((resultReceipt - acknowledgmentReceipt) * 1000) / estimate;
            return checked(checked((policy.EmaDenominator - policy.EmaNumerator) * prior) + checked(policy.EmaNumerator * sample)) / policy.EmaDenominator;
        }
    }
}
