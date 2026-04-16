using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnWaterBlocks : MonoBehaviour
{
   
    public GameObject waterBlockPrefab;

    private void spawnWaterBlocks()
    {
        
        int k = 40; 
        for (int i = -k; i <= k; i++)
        {
            for (int j = -k; j <= k; j++)
            {
                GameObject a = Instantiate(waterBlockPrefab) as GameObject;
                a.transform.position = new Vector3((float)(i*49), (float)(1), (float)(j*49));
            }
        }
        
    }
    

    // Start is called before the first frame update
    void Start()
    {
        spawnWaterBlocks();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
}
