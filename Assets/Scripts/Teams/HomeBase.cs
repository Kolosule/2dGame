using UnityEngine;

public class HomeBase : MonoBehaviour
{
    [SerializeField] private string teamName = "Team1"; // set in inspector
    public string TeamName => teamName;
}