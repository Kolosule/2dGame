using UnityEngine;
using TMPro;

/// <summary>
/// Coin count / total value readout. Event-driven — repaints only on
/// NetworkedPlayerInventory.CoinsChanged.
/// </summary>
public class CoinDisplay : MonoBehaviour, IHudBindable
{
    [Tooltip("Coin count text, e.g. the number carried.")]
    [SerializeField] private TextMeshProUGUI coinCountText;

    [Tooltip("Optional total coin value text.")]
    [SerializeField] private TextMeshProUGUI coinValueText;

    private NetworkedPlayerInventory inventory;

    public void Bind(HudContext ctx)
    {
        inventory = ctx.Inventory;
        if (inventory == null) return;
        inventory.CoinsChanged += Repaint;
        Repaint();
    }

    public void Unbind()
    {
        if (inventory != null) inventory.CoinsChanged -= Repaint;
        inventory = null;
    }

    private void Repaint()
    {
        if (inventory == null) return;
        if (coinCountText != null) coinCountText.text = inventory.CoinCount.ToString();
        if (coinValueText != null) coinValueText.text = inventory.TotalCoinValue.ToString();
    }

    private void OnDisable() => Unbind();
}
