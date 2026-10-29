using System;

namespace Station.Contracts
{
    public sealed class AuthorityConfig
    {
        public long SlackMinimum { get; }
        public long SlackMargin { get; }
        public long SlackMaximum { get; }
        public long GraceTicks { get; }
        public long EmaNumerator { get; }
        public long EmaDenominator { get; }
        public long UnacknowledgedTicks { get; }
        public long IncidentAdmissionPermille { get; }
        public int MaximumReplacementDepth { get; }
        public long ResponseOrderTicks { get; }
        public string PatrolRole { get; }
        public AuthorityConfig(long slackMinimum = 1500, long slackMargin = 250, long slackMaximum = 4000, long graceTicks = 3000,
            long emaNumerator = 1, long emaDenominator = 8, long unacknowledgedTicks = 36000,
            long incidentAdmissionPermille = 50, int maximumReplacementDepth = 2, long responseOrderTicks = 1728000, string patrolRole = "guard")
        {
            if (slackMinimum < 1 || slackMargin < 0 || slackMaximum < slackMinimum || graceTicks < 0 || emaNumerator < 1 || emaDenominator < emaNumerator || unacknowledgedTicks < 1 || incidentAdmissionPermille < 0 || incidentAdmissionPermille > 1000 || maximumReplacementDepth < 0 || responseOrderTicks < 1)
                throw new ArgumentException("Invalid authority deadline/EMA policy.");
            SlackMinimum = slackMinimum; SlackMargin = slackMargin; SlackMaximum = slackMaximum; GraceTicks = graceTicks;
            EmaNumerator = emaNumerator; EmaDenominator = emaDenominator; UnacknowledgedTicks = unacknowledgedTicks;
            IncidentAdmissionPermille = incidentAdmissionPermille; MaximumReplacementDepth = maximumReplacementDepth; ResponseOrderTicks = responseOrderTicks;
            if (string.IsNullOrWhiteSpace(patrolRole)) throw new ArgumentException("Patrol role is required."); PatrolRole = patrolRole;
        }
    }
    public sealed class KnownPersonnel
    {
        public long ActorId { get; }
        public long AccountId { get; }
        public string Role { get; }
        public RosterPayload? Roster { get; }
        public KnownPersonnel(long actorId, long accountId, string role, RosterPayload? roster = null)
        { if (actorId < 1 || accountId < 1 || string.IsNullOrWhiteSpace(role)) throw new ArgumentException("Invalid personnel registration."); ActorId = actorId; AccountId = accountId; Role = role; Roster = roster; }
    }
    public sealed class WorkDurationEstimate
    {
        public long TravelToJob { get; }
        public long WorkTicks { get; }
        public long TravelToTerminal { get; }
        public long ReturnTerminalId { get; }
        public long Total { get; }
        public WorkDurationEstimate(long travelToJob, long workTicks, long travelToTerminal, long returnTerminalId, long total)
        { TravelToJob = travelToJob; WorkTicks = workTicks; TravelToTerminal = travelToTerminal; ReturnTerminalId = returnTerminalId; Total = total; }
    }
    public sealed class ReportDeadline
    {
        public long Slack { get; }
        public long BaseTick { get; }
        public long OffDutyExtension { get; }
        public long Tick { get; }
        public ReportDeadline(long slack, long baseTick, long extension)
        { Slack = slack; BaseTick = baseTick; OffDutyExtension = extension; Tick = checked(baseTick + extension); }
    }
}
