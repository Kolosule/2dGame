using UnityEngine;

public class Coin : MonoBehaviour
{
    [SerializeField] private int value = 1; // how much gold this coin is worth

    public int Value => value;

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerCoinCollector collector = other.GetComponent<PlayerCoinCollector>();
        if (collector != null)
        {
            collector.CollectCoin(this);
        }
    }
}