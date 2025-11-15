using UnityEngine;

public class ForceUIOn : MonoBehaviour
{
    public GameObject[] panels;

    private void Awake()
    {
        //all assigned panels are ON when the scene starts
        foreach (var p in panels)
        {
            if (p != null) p.SetActive(true);
        }
    }
}
