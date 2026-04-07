namespace Cip.Contracts.Constants;

public static partial class Constants
{
    public static class Events
    {
        public const string Received = "Received";
        public const string PendingApproval = "PendingApproval";
        public const string Applied = "Applied";
        public const string Rejected = "Rejected";
    }

    public static class Profiles
    {
        public const string Shell = "profile";
        public const string PendingReview = "PendingReview";
        public const string Ready = "Ready";
        public const int ProfileCardMaxLength = 600;
    }

    public static class ChangeSets
    {
        public const string EventMaterialization = "EventMaterialization";
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public static class Triggers
    {
        public const string Active = "Active";
        public const string TraitEquals = "TraitEquals";
        public const string IdentityEquals = "IdentityEquals";
        public const string IdentityContains = "IdentityContains";
    }

    public static class Runtime
    {
        public const string Auto = "Auto";
        public const string Cosmos = "Cosmos";
        public const string InMemory = "InMemory";
    }
}
