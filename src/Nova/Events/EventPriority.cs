namespace Nova.Events;

public enum EventPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum EventCategory
{
    General,
    System,
    UserAction,
    Companion,
    Test
}

