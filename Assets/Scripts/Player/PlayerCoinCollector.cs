using UnityEngine;

public class PlayerCoinCollector : MonoBehaviour
{
    public int carriedCoins = 0;
    public int bankedCoins = 0;

    public string TeamName { get; private set; } // assigned by SpawnManager

    public void SetTeamName(string name)
    {
        TeamName = name;
    }

    public void CollectCoin(Coin coin)
    {
        carriedCoins += coin.Value;
        Debug.Log($"Picked up coin! Carried: {carriedCoins}");
        Destroy(coin.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HomeBase baseObj = other.GetComponent<HomeBase>();
        if (baseObj != null && baseObj.TeamName == TeamName)
        {
            DepositCoins();
        }
    }

    private void DepositCoins()
    {
        bankedCoins += carriedCoins;
        Debug.Log($"{TeamName} deposited {carriedCoins} coins! Total banked: {bankedCoins}");
        carriedCoins = 0;
    }
}