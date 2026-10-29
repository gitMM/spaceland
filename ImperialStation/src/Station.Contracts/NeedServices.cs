using System;

namespace Station.Contracts
{
    public enum NeedServiceKind { Cafeteria, Toilet, RestSpot, Bed }
    public sealed class NeedServiceDefinition
    {
        public long Id { get; }
        public NeedServiceKind Kind { get; }
        public Coord Position { get; }
        public int Capacity { get; }
        public long DurationTicks { get; }
        public string MealResource { get; }
        public long AssignedActorId { get; }
        public long DetentionFacilityId { get; }
        public bool Enabled { get; }
        public long AllocationTicks { get; }
        public bool AllocationRequiresCredential { get; }
        public NeedServiceDefinition(long id, NeedServiceKind kind, Coord position, int capacity = 1, long durationTicks = 0, string mealResource = "meal", long assignedActorId = 0, long detentionFacilityId = 0, bool enabled = true, long allocationTicks = 100, bool allocationRequiresCredential = true)
        {
            if (id < 1 || !Enum.IsDefined(typeof(NeedServiceKind), kind) || capacity < 1 || allocationTicks < 1 || durationTicks < 0 || assignedActorId < 0 || detentionFacilityId < 0 ||
                kind == NeedServiceKind.Bed && capacity != 1 || kind != NeedServiceKind.Bed && assignedActorId != 0 || string.IsNullOrWhiteSpace(mealResource)) throw new ArgumentException("Invalid need service.");
            Id = id; Kind = kind; Position = position; Capacity = capacity; DurationTicks = durationTicks > 0 ? durationTicks : kind switch { NeedServiceKind.Cafeteria => 9000, NeedServiceKind.Toilet => 1800, NeedServiceKind.RestSpot => 6000, _ => 288000 };
            MealResource = mealResource; AssignedActorId = assignedActorId; DetentionFacilityId = detentionFacilityId; Enabled = enabled; AllocationTicks = allocationTicks; AllocationRequiresCredential = allocationRequiresCredential;
        }
        public ActionKind Action => Kind switch { NeedServiceKind.Cafeteria => ActionKind.Eat, NeedServiceKind.Toilet => ActionKind.UseToilet, NeedServiceKind.RestSpot => ActionKind.Rest, _ => ActionKind.Sleep };
    }
}
