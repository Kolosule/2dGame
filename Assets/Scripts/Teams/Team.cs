/// <summary>
/// Canonical team identity for the whole game. The single networked source of truth lives on
/// PlayerTeamData as a [Networked] Team. Underlying ints map to the legacy team numbers
/// (1 = Team1/Blue, 2 = Team2/Red, 3 = Team3 AI) so conversion stays trivial.
/// </summary>
public enum Team
{
    None = 0,
    Team1 = 1,
    Team2 = 2,
    Team3AI = 3
}
