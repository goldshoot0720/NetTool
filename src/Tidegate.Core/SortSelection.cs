namespace Tidegate.Core;

public readonly record struct SortSelection(SortKey Key, SortDirection Direction, SpeedBasis Basis)
{
    public static SortSelection NameAscending { get; } = new(SortKey.Name, SortDirection.Ascending, SpeedBasis.Total);

    public static SortSelection SpeedDescending { get; } = new(SortKey.Speed, SortDirection.Descending, SpeedBasis.Total);

    public SortSelection ToggleName() =>
        Key == SortKey.Name
            ? this with { Direction = Opposite(Direction) }
            : new SortSelection(SortKey.Name, SortDirection.Ascending, Basis);

    public SortSelection ToggleSpeed(SpeedBasis basis) =>
        Key == SortKey.Speed && Basis == basis
            ? this with { Direction = Opposite(Direction) }
            : new SortSelection(SortKey.Speed, SortDirection.Descending, basis);

    public static SortDirection Opposite(SortDirection direction) =>
        direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
}
