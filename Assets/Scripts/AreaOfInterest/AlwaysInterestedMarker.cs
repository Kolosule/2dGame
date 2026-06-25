using UnityEngine;

/// <summary>
/// Tag for a GameObject (carrying a NetworkObject) that must be replicated to EVERY player
/// regardless of Area-of-Interest distance — e.g. the flags, the CTF/score managers, home bases.
/// AreaOfInterestRegistrar finds every marker at startup and registers its NetworkObject as
/// always-interested for all players (including late joiners). Marking is the single explicit
/// place AoI culling is overridden, so HUD/objective state never disappears for distant players.
/// </summary>
public class AlwaysInterestedMarker : MonoBehaviour
{
}
